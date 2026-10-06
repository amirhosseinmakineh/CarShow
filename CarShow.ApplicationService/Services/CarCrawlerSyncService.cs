using System.Text.Json;
using CarShow.Domain.Crawling;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

public sealed class CarCrawlerSyncService : ICarCrawlerSyncService
{
    private readonly ICarDataSource source;
    private readonly IBaseRepository<long, Company> companies;
    private readonly IBaseRepository<long, CarModel> models;
    private readonly IBaseRepository<long, Tip> tips;
    private readonly IBaseRepository<long, Car> cars;
    private readonly IBaseRepository<long, CarPriceHistory> history;

    public CarCrawlerSyncService(ICarDataSource source,
        IBaseRepository<long, Company> companies,
        IBaseRepository<long, CarModel> models,
        IBaseRepository<long, Tip> tips,
        IBaseRepository<long, Car> cars,
        IBaseRepository<long, CarPriceHistory> history)
    {
        this.source = source;
        this.companies = companies;
        this.models = models;
        this.tips = tips;
        this.cars = cars;
        this.history = history;
    }

    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        var rows = await source.FetchAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.CompanyName) ||
                string.IsNullOrWhiteSpace(row.CarName) ||
                string.IsNullOrWhiteSpace(row.SourceUrl)) continue;

            var company = await companies.GetAll().FirstOrDefaultAsync(x => x.Name == row.CompanyName, cancellationToken);
            if (company is null)
            {
                company = new Company { Name = row.CompanyName };
                await companies.Create(company);
                await companies.SaveChanges();
            }
            var modelName = string.IsNullOrWhiteSpace(row.ModelName) ? row.CarName : row.ModelName;
            var model = await models.GetAll().FirstOrDefaultAsync(x => x.Name == modelName, cancellationToken);
            if (model is null)
            {
                model = new CarModel { Name = modelName };
                await models.Create(model);
                await models.SaveChanges();
            }
            Tip? tip = null;
            if (!string.IsNullOrWhiteSpace(row.TipName))
            {
                tip = await tips.GetAll().FirstOrDefaultAsync(x => x.Name == row.TipName, cancellationToken);
                if (tip is null)
                {
                    tip = new Tip { Name = row.TipName };
                    await tips.Create(tip);
                    await tips.SaveChanges();
                }
            }

            // Name retains the year and variant; a shared detail URL must not merge price rows.
            var car = await cars.GetAll().FirstOrDefaultAsync(x =>
                x.SourceUrl == row.SourceUrl && x.Name == row.CarName, cancellationToken);
            var isNew = car is null;
            car ??= new Car { Name = row.CarName, SourceUrl = row.SourceUrl };
            var changed = isNew || car.MarketPrice != row.MarketPrice || car.FactoryPrice != row.FactoryPrice;

            car.CompanyId = company.Id;
            car.CarModelId = model.Id;
            car.TipId = tip?.Id;
            car.MarketPrice = row.MarketPrice;
            car.FactoryPrice = row.FactoryPrice;
            car.LastUpdated = DateTime.UtcNow;
            car.Slug = new Uri(row.SourceUrl).AbsolutePath.Trim('/');
            if (row.Details is not null)
            {
                // Versioned structured payload in the existing text column: no schema change.
                car.Description = JsonSerializer.Serialize(row.Details);
                if (!string.IsNullOrWhiteSpace(row.Details.ImageUrl))
                    car.ImageName = row.Details.ImageUrl;
            }
            if (isNew) await cars.Create(car);
            else await cars.Update(car);
            await cars.SaveChanges();

            if (changed)
            {
                await history.Create(new CarPriceHistory
                {
                    CarId = car.Id, MarketPrice = car.MarketPrice,
                    FactoryPrice = car.FactoryPrice, Date = car.LastUpdated
                });
                await history.SaveChanges();
            }
            count++;
        }
        return count;
    }
}
