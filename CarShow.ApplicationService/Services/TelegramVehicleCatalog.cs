using System.Security.Cryptography;
using System.Text;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

/// <summary>Per-cycle catalog cache; all database reads are batched before vehicle upserts.</summary>
internal sealed class TelegramVehicleCatalog
{
    private readonly ITelegramProcessingStore store;
    private readonly List<Car> cars;
    private readonly Dictionary<string, Car> vehicles;
    private readonly Dictionary<string, Company> companies;
    private readonly Dictionary<string, CarModel> models;
    private readonly Dictionary<string, Tip> trims;

    private TelegramVehicleCatalog(ITelegramProcessingStore store, List<Car> cars,
        List<Company> companies, List<CarModel> models, List<Tip> trims)
    {
        this.store = store;
        this.cars = cars;
        vehicles = cars.Where(car => car.TelegramIdentityKey != null)
            .GroupBy(car => car.TelegramIdentityKey!).ToDictionary(group => group.Key, group => group.First());
        this.companies = companies.GroupBy(company => CarListingMatcher.Normalize(company.Name))
            .ToDictionary(group => group.Key, group => group.First());
        this.models = models.GroupBy(model => CarListingMatcher.Normalize(model.Name))
            .ToDictionary(group => group.Key, group => group.First());
        this.trims = trims.GroupBy(trim => CarListingMatcher.NormalizeTrim(trim.Name))
            .ToDictionary(group => group.Key, group => group.First());
    }

    public IReadOnlyList<Car> Cars => cars;

    public static async Task<TelegramVehicleCatalog> LoadAsync(ITelegramProcessingStore store, IReadOnlyList<Car> existingCars, CancellationToken cancellationToken)
    {
        // A scoped EF context must not execute concurrent queries.
        var cars = existingCars.ToList();
        var companies = await store.Companies.Where(company => !company.IsDelete).ToListAsync(cancellationToken);
        var models = await store.CarModels.Where(model => !model.IsDelete).ToListAsync(cancellationToken);
        var trims = await store.Tips.Where(trim => !trim.IsDelete).ToListAsync(cancellationToken);
        return new TelegramVehicleCatalog(store, cars, companies, models, trims);
    }

    public Car GetOrCreate(CarListing listing, DateTime nowUtc)
    {
        var identity = listing.ComparisonKey!;
        if (vehicles.TryGetValue(identity, out var existing))
        {
            return existing;
        }

        var companyName = listing.Manufacturer?.Trim();
        var modelName = listing.Model!.Trim();
        var trimName = listing.Trim?.Trim();
        Company? company = null;
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            var companyKey = CarListingMatcher.Normalize(companyName);
            if (!companies.TryGetValue(companyKey, out company))
            {
                company = new Company { Name = companyName, IsDelete = false };
                companies.Add(companyKey, company);
                store.Add(company);
            }
        }
        var modelKey = CarListingMatcher.Normalize(modelName);
        if (!models.TryGetValue(modelKey, out var model))
        {
            model = new CarModel { Name = modelName, IsDelete = false };
            models.Add(modelKey, model);
            store.Add(model);
        }
        Tip? trim = null;
        if (!string.IsNullOrWhiteSpace(trimName))
        {
            var trimKey = CarListingMatcher.NormalizeTrim(trimName);
            if (!trims.TryGetValue(trimKey, out trim))
            {
                trim = new Tip { Name = trimName, IsDelete = false };
                trims.Add(trimKey, trim);
                store.Add(trim);
            }
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        var car = new Car
        {
            Company = company,
            CarModel = model,
            Tip = trim,
            IsUsed = listing.IsUsed,
            Year = listing.Year,
            YearCalendar = listing.YearCalendar,
            TelegramIdentityKey = identity,
            Name = string.Join(" ", new[] { companyName, modelName, trimName, listing.Year?.ToString() }
                .Where(part => !string.IsNullOrWhiteSpace(part))),
            Slug = $"telegram-{digest}",
            ImageName = listing.PrimaryImageUrl ?? string.Empty,
            SourceUrl = listing.SourceUrl!,
            LastUpdated = nowUtc,
            MarketPrice = 0,
            FactoryPrice = 0,
            PriceSampleCount = 0,
            IsDelete = false
        };
        vehicles.Add(identity, car);
        cars.Add(car);
        store.Add(car);
        return car;
    }
}
