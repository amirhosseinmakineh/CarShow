using System.Text.Json;
using CarShow.Domain.Crawling;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarShow.ApplicationService.Services;

public sealed class CarCrawlerSyncService : ICarCrawlerSyncService
{
    private readonly ICarDataSource source;
    private readonly IBaseRepository<long, Company> companies;
    private readonly IBaseRepository<long, CarModel> models;
    private readonly IBaseRepository<long, Tip> tips;
    private readonly IBaseRepository<long, Car> cars;
    private readonly IBaseRepository<long, CarPriceHistory> history;
    private readonly ILogger<CarCrawlerSyncService> logger;

    public CarCrawlerSyncService(
        ICarDataSource source,
        IBaseRepository<long, Company> companies,
        IBaseRepository<long, CarModel> models,
        IBaseRepository<long, Tip> tips,
        IBaseRepository<long, Car> cars,
        IBaseRepository<long, CarPriceHistory> history,
        ILogger<CarCrawlerSyncService> logger)
    {
        this.source = source;
        this.companies = companies;
        this.models = models;
        this.tips = tips;
        this.cars = cars;
        this.history = history;
        this.logger = logger;
    }

    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        var rows = await source.FetchAsync(cancellationToken);
        var count = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (string.IsNullOrWhiteSpace(row.CompanyName) ||
                    string.IsNullOrWhiteSpace(row.CarName) ||
                    string.IsNullOrWhiteSpace(row.SourceUrl))
                    continue;

                await SyncRowAsync(row, cancellationToken);
                count++;
            }
            catch (Exception ex)
            {
                // One bad vehicle must not stop all other brands.
                logger.LogError(ex,
                    "Car synchronization failed. Company={Company}, Car={Car}, SourceUrl={SourceUrl}",
                    row.CompanyName, row.CarName, row.SourceUrl);
            }
        }

        // Enrich already-saved cars afterwards. Detail failures never remove the price row.
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.SourceUrl))
                continue;

            try
            {
                var details = await source.FetchDetailsAsync(row.SourceUrl, cancellationToken);
                if (details is null)
                    continue;

                row.Details = details;
                row.TipName = details.Sections
                    .SelectMany(x => x.Specifications)
                    .Where(x => x.Name.Contains("تیپ", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                await SyncRowAsync(row, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Detail enrichment failed. Company={Company}, Car={Car}, SourceUrl={SourceUrl}",
                    row.CompanyName, row.CarName, row.SourceUrl);
            }
        }

        logger.LogWarning("Car synchronization finished. {Processed}/{Total} price rows processed.", count, rows.Count);
        return count;
    }

    private async Task SyncRowAsync(CarSourceData row, CancellationToken cancellationToken)
    {
        var company = await companies.GetAll()
            .FirstOrDefaultAsync(x => x.Name == row.CompanyName, cancellationToken);

        if (company is null)
        {
            company = new Company { Name = row.CompanyName };
            await companies.Create(company);
            await companies.SaveChanges();
        }

        var modelName = string.IsNullOrWhiteSpace(row.ModelName) ? row.CarName : row.ModelName;
        var model = await models.GetAll()
            .FirstOrDefaultAsync(x => x.Name == modelName, cancellationToken);

        if (model is null)
        {
            model = new CarModel { Name = modelName };
            await models.Create(model);
            await models.SaveChanges();
        }

        Tip? tip = null;
        if (!string.IsNullOrWhiteSpace(row.TipName))
        {
            tip = await tips.GetAll()
                .FirstOrDefaultAsync(x => x.Name == row.TipName, cancellationToken);

            if (tip is null)
            {
                tip = new Tip { Name = row.TipName };
                await tips.Create(tip);
                await tips.SaveChanges();
            }
        }

        var car = await cars.GetAll()
            .FirstOrDefaultAsync(x => x.SourceUrl == row.SourceUrl && x.Name == row.CarName, cancellationToken);

        var isNew = car is null;
        car ??= new Car { Name = row.CarName, SourceUrl = row.SourceUrl };

        var changed = isNew ||
                      car.MarketPrice != row.MarketPrice ||
                      car.FactoryPrice != row.FactoryPrice;

        car.CompanyId = company.Id;
        car.CarModelId = model.Id;
        if (tip is not null)
            car.TipId = tip.Id;

        car.MarketPrice = row.MarketPrice;
        car.FactoryPrice = row.FactoryPrice;
        car.LastUpdated = DateTime.UtcNow;

        if (Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out var sourceUri))
            car.Slug = sourceUri.AbsolutePath.Trim('/');

        if (row.Details is not null)
        {
            car.Description = JsonSerializer.Serialize(row.Details);
            if (!string.IsNullOrWhiteSpace(row.Details.ImageUrl))
                car.ImageName = row.Details.ImageUrl;
        }

        if (isNew)
            await cars.Create(car);
        else
            await cars.Update(car);

        await cars.SaveChanges();

        if (!changed)
            return;

        try
        {
            await history.Create(new CarPriceHistory
            {
                CarId = car.Id,
                MarketPrice = car.MarketPrice,
                FactoryPrice = car.FactoryPrice,
                Date = car.LastUpdated
            });
            await history.SaveChanges();
        }
        catch (Exception ex)
        {
            // Price history must not prevent the current car/price from being available.
            logger.LogError(ex,
                "Price history could not be saved for CarId={CarId}. Current car data was saved.",
                car.Id);
        }
    }
}
