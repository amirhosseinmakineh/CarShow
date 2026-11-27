using System.Text.Json;
using CarShow.Infrastracture.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/catalog")]
public sealed class AdminCatalogController : ControllerBase
{
    private readonly CarShowContext _db;

    public AdminCatalogController(CarShowContext db) => _db = db;

    // Show all non-deleted cars in the admin catalog.
    [HttpGet]
    public async Task<IActionResult> GetCatalog(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 0,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(0, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Counts and pagination include all non-deleted cars, regardless of reference approval.
        var all = _db.Cars.AsNoTracking().Where(x => !x.IsDelete);
        var totalCars = await all.CountAsync(cancellationToken);
        var eligibleCars = await all.CountAsync(x =>
            x.CarModelId != null && x.TipId != null, cancellationToken);
        var missingCompany = await all.CountAsync(x =>
            x.CompanyId == null, cancellationToken);

        var query = all;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var matchesId = long.TryParse(term, out var id);
            query = query.Where(x => (matchesId && x.Id == id) || x.Name.Contains(term)
                || (x.CarModel != null && x.CarModel.Name.Contains(term))
                || (x.Tip != null && x.Tip.Name.Contains(term))
                || (x.Company != null && x.Company.Name.Contains(term)));
        }

        var filteredCount = await query.CountAsync(cancellationToken);
        var cars = await query
            .OrderBy(x => x.Id)
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.ImageName,
                x.IsReferenceApproved,
                x.Description,
                x.FactoryPrice,
                x.MarketPrice,
                x.Name,
                x.CompanyId,
                Company = x.Company != null ? x.Company.Name : null,
                x.CarModelId,
                Model = x.CarModel != null ? x.CarModel.Name : null,
                x.TipId,
                Tip = x.Tip != null ? x.Tip.Name : null,
                EligibleForTelegramComparison = x.CarModelId != null && x.TipId != null,
                x.LastUpdated
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            TotalCars = totalCars,
            EligibleCars = eligibleCars,
            ExcludedCars = totalCars - eligibleCars,
            MissingCompany = missingCompany,
            FilteredCount = filteredCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Items = cars.Select(car =>
            {
                var (year, trim) = ReadScanMetadata(car.Description);
                return new
                {
                    car.Id,
                    car.ImageName,
                    car.IsReferenceApproved,
                    car.Description,
                    car.FactoryPrice,
                    car.MarketPrice,
                    StartDate = year,
                    car.Name,
                    car.CompanyId,
                    car.Company,
                    car.CarModelId,
                    car.Model,
                    car.TipId,
                    Tip = trim ?? car.Tip,
                    car.EligibleForTelegramComparison,
                    car.LastUpdated
                };
            }).ToList()
        });
    }

    private static (string Year, string? Trim) ReadScanMetadata(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (string.Empty, null);

        try
        {
            using var document = JsonDocument.Parse(description);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "TelegramPriceReference")
                return (string.Empty, null);

            var year = root.TryGetProperty("Year", out var yearValue) &&
                yearValue.ValueKind is JsonValueKind.String or JsonValueKind.Number
                ? yearValue.ToString()
                : string.Empty;
            var trim = root.TryGetProperty("TipName", out var trimValue) && trimValue.ValueKind == JsonValueKind.String
                ? trimValue.GetString()
                : null;
            return (year, string.IsNullOrWhiteSpace(trim) ? null : trim);
        }
        catch (JsonException)
        {
            return (string.Empty, null);
        }
    }
}
