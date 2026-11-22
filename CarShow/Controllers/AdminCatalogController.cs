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

    // Show only currently approved reference cars in the admin listing.
    [HttpGet]
    public async Task<IActionResult> GetCatalog(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 0,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(0, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Counts and pagination must reflect the approved Cars records, not all catalog rows.
        // The separate model-image management endpoint still exposes all model/trim options.
        var all = _db.Cars.AsNoTracking().Where(x => !x.IsDelete && x.IsReferenceApproved);
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
                StartDate = string.Empty,
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
            Items = cars
        });
    }
}
