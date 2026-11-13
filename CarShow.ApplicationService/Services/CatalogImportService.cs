using CarShow.ApplicationService.Contract.Dtos.CarDto;
using CarShow.ApplicationService.Import;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

public sealed class CatalogImportService(
    IBaseRepository<long, Car> cars,
    IBaseRepository<long, Company> companiesRepository,
    IBaseRepository<long, CarModel> modelsRepository,
    IBaseRepository<long, Tip> tipsRepository,
    IUnitOfWork unitOfWork)
{
    private static string Key(string value) => string.Join(" ", value.Replace('ي', 'ی').Replace('ك', 'ک')
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public async Task<CatalogImportResultDto> ImportAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, true);
        var entries = CatalogHtmlParser.Parse(await reader.ReadToEndAsync(cancellationToken));
        var accepted = entries.Where(x => !string.IsNullOrWhiteSpace(x.Company)
            && !string.IsNullOrWhiteSpace(x.Model) && !string.IsNullOrWhiteSpace(x.Tip)).ToList();
        var companies = (await companiesRepository.GetAll().ToListAsync(cancellationToken))
            .GroupBy(x => Key(x.Name)).ToDictionary(x => x.Key, x => x.First());
        var models = (await modelsRepository.GetAll().ToListAsync(cancellationToken))
            .GroupBy(x => Key(x.Name)).ToDictionary(x => x.Key, x => x.First());
        var tips = (await tipsRepository.GetAll().ToListAsync(cancellationToken))
            .GroupBy(x => Key(x.Name)).ToDictionary(x => x.Key, x => x.First());
        int createdCompanies = 0, createdModels = 0, createdTips = 0, imported = 0, existing = 0;
        await unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var entry in accepted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var companyKey = Key(entry.Company);
                if (!companies.ContainsKey(companyKey))
                {
                    var company = new Company { Name = entry.Company };
                    companies.Add(companyKey, company);
                    await companiesRepository.Create(company);
                    createdCompanies++;
                }
                var modelKey = Key(entry.Model);
                if (!models.ContainsKey(modelKey))
                {
                    var model = new CarModel { Name = entry.Model };
                    models.Add(modelKey, model);
                    await modelsRepository.Create(model);
                    createdModels++;
                }
                var tipKey = Key(entry.Tip);
                if (!tips.ContainsKey(tipKey))
                {
                    var tip = new Tip { Name = entry.Tip };
                    tips.Add(tipKey, tip);
                    await tipsRepository.Create(tip);
                    createdTips++;
                }
            }
            await unitOfWork.SaveChangesAsync();
            var known = (await cars.GetAll().Where(x => x.CompanyId != null && x.CarModelId != null && x.TipId != null)
                .Select(x => new { x.CompanyId, x.CarModelId, x.TipId }).ToListAsync(cancellationToken))
                .Select(x => (x.CompanyId, x.CarModelId, x.TipId)).ToHashSet();
            foreach (var entry in accepted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var companyId = companies[Key(entry.Company)].Id;
                var modelId = models[Key(entry.Model)].Id;
                var tipId = tips[Key(entry.Tip)].Id;
                if (!known.Add((companyId, modelId, tipId))) { existing++; continue; }
                await cars.Create(new Car
                {
                    Name = $"{entry.Brand} {entry.Model} {entry.Tip}".Trim(),
                    CompanyId = companyId,
                    CarModelId = modelId,
                    TipId = tipId,
                    Slug = $"catalog-{companyId}-{modelId}-{tipId}",
                    SourceUrl = entry.SourceUrl,
                    MarketPrice = 0m,
                    FactoryPrice = 0m,
                    LastUpdated = DateTime.UtcNow,
                    ImageName = string.Empty,
                    Description = string.Empty
                });
                imported++;
            }
            await unitOfWork.CommitAsync();
            return new(entries.Count, imported, existing, entries.Count - accepted.Count,
                createdModels, createdCompanies, createdTips);
        }
        catch
        {
            await unitOfWork.RollbackAsync();
            throw;
        }
    }
}
