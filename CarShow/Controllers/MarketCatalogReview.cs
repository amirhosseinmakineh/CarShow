using System.ComponentModel.DataAnnotations;
using CarShow.ApplicationService.Services;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.Models.ZeroVehicles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Controllers;

public sealed record MarketCatalogModelRequest([Required, MaxLength(128)] string Name,
    [MaxLength(2000)] string Aliases, long? CompanyId, [Required, MaxLength(1024)] string Reason);
public sealed record MarketCatalogTrimRequest(long ModelId, [Required, MaxLength(128)] string Name,
    [MaxLength(2000)] string Aliases, string? Transmission, [Required, MaxLength(1024)] string Reason);

public sealed partial class MarketReviewController
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(new {
        companies = await db.Companies.AsNoTracking().Where(x => !x.IsDelete).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct),
        models = await db.Set<ZeroVehicleModel>().AsNoTracking().OrderBy(x => x.Name).Select(x => new {
            x.Id, x.Name, x.Aliases, x.CompanyId, trims = x.Trims.Select(t => new { t.Id, t.Name, t.Aliases, t.Transmission }) }).ToListAsync(ct)
    });

    [HttpPut("catalog/models/{id:long}")]
    public async Task<IActionResult> Model(long id, MarketCatalogModelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Reason)) return BadRequest();
        if (request.CompanyId.HasValue && !await db.Companies.AnyAsync(x => x.Id == request.CompanyId && !x.IsDelete, ct)) return BadRequest();
        var found = false;
        await usedStore.RunExclusiveAsync("TelegramDailyPricing", async () => {
            await LockZeroAsync(ct);
            {
                var model = id == 0 ? new ZeroVehicleModel() : await db.Set<ZeroVehicleModel>().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (model is null) return;
                found = true;
                var before = new { model.Name, model.Aliases, model.CompanyId };
                model.Name = request.Name.Trim(); model.Aliases = request.Aliases ?? ""; model.CompanyId = request.CompanyId;
                if (id == 0) { db.Add(model); model.Trims.Add(new() { Name = "نام‌محور" }); }
                await db.SaveChangesAsync(ct);
                Audit("catalog-model", model.Id, "Save", request.Reason, before, new { model.Name, model.Aliases, model.CompanyId });
                await QueueModelRecheckAsync(model.Name, ct, before.Name, model.Id);
            }
        }, ct);
        return found ? NoContent() : NotFound();
    }

    [HttpPut("catalog/trims/{id:long}")]
    public async Task<IActionResult> Trim(long id, MarketCatalogTrimRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Transmission is not (null or "" or "Automatic" or "Manual")
            || !await db.Set<ZeroVehicleModel>().AnyAsync(x => x.Id == request.ModelId, ct)) return BadRequest();
        var found = false;
        await usedStore.RunExclusiveAsync("TelegramDailyPricing", async () => {
            await LockZeroAsync(ct);
            {
                var trim = id == 0 ? new ZeroVehicleTrim { ModelId = request.ModelId }
                    : await db.Set<ZeroVehicleTrim>().SingleOrDefaultAsync(x => x.Id == id && x.ModelId == request.ModelId, ct);
                if (trim is null) return;
                if (trim.Name == "نام‌محور" && request.Name != trim.Name) return;
                found = true; var before = new { trim.Name, trim.Aliases, trim.Transmission };
                trim.Name = request.Name.Trim(); trim.Aliases = request.Aliases ?? ""; trim.Transmission = request.Transmission;
                if (id == 0) db.Add(trim);
                await db.SaveChangesAsync(ct);
                Audit("catalog-trim", trim.Id, "Save", request.Reason, before, new { trim.Name, trim.Aliases, trim.Transmission });
                var modelName = await db.Set<ZeroVehicleModel>().Where(x => x.Id == request.ModelId).Select(x => x.Name).SingleAsync(ct);
                await QueueModelRecheckAsync(modelName, ct, modelName, request.ModelId);
            }
        }, ct);
        return found ? NoContent() : NotFound();
    }

    [HttpGet("unknown")]
    public async Task<IActionResult> Unknown(CancellationToken ct, int page = 0)
    {
        var query = db.CarListings.AsNoTracking().Where(x => !x.IsDelete && (x.Model == null || x.Trim == null || x.MatchStatus == "NeedsReview"));
        return Ok(new { total = await query.CountAsync(ct), page, pageSize = 20, items = await query.OrderByDescending(x => x.Id)
            .Skip(Math.Clamp(page, 0, 100000) * 20).Take(20).Select(x => new { x.Id, x.RawCarName, x.Model, x.Trim, x.Year,
                x.YearCalendar, x.Mileage, x.IsUsed, x.Price, x.Currency, x.SourceChannel, x.SourceUrl, x.ReasonCode, x.MatchReason }).ToListAsync(ct) });
    }

    [HttpPut("listings/{scope}/{id:long}/identity")]
    public async Task<IActionResult> Identity(string scope, long id, MarketIdentityRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Mileage is < 0
            || request.Year.HasValue && !((request.Year is >= 1300 and <= 1500 && request.YearCalendar == "Jalali")
                || (request.Year is >= 1900 and <= 2100 && request.YearCalendar == "Gregorian"))
            || !request.Year.HasValue && request.YearCalendar != null
            || request.Transmission is not (null or "Automatic" or "Manual")
            || request.BodyCondition is { Length: > 64 }) return BadRequest();
        var model = await db.Set<ZeroVehicleModel>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ModelId, ct);
        if (model is null) return BadRequest();
        var trim = await db.Set<ZeroVehicleTrim>().AsNoTracking().SingleOrDefaultAsync(x => x.ModelId == model.Id
            && (request.TrimId.HasValue ? x.Id == request.TrimId : x.Name == "نام‌محور"), ct);
        if (trim is null || scope == "zero" && request.Mileage > 0) return BadRequest();
        var knownTransmission = trim.Transmission ?? (CarListingMatcher.NormalizeTrim(trim.Name).Contains("اتومات", StringComparison.Ordinal)
            ? "Automatic" : CarListingMatcher.NormalizeTrim(trim.Name).Contains("دنده", StringComparison.Ordinal) ? "Manual" : null);
        if (request.Transmission != null && knownTransmission != null && request.Transmission != knownTransmission) return BadRequest();
        var found = false;
        if (scope == "used") await usedStore.RunExclusiveAsync("TelegramDailyPricing", async () => {
            var listing = await db.CarListings.SingleOrDefaultAsync(x => x.Id == id && !x.IsDelete, ct); if (listing is null) return;
            found = true;
            var before = new { listing.Model, listing.Trim, listing.Year, listing.YearCalendar, listing.Mileage, listing.Transmission, listing.BodyCondition };
            await InvalidateUsedAsync(listing, ct);
            listing.Model = CarListingMatcher.Normalize(model.Name); listing.Trim = trim.Name == "نام‌محور" ? null : trim.Name;
            listing.Year = request.Year; listing.YearCalendar = request.YearCalendar; listing.Mileage = request.Mileage;
            listing.Transmission = request.Transmission; listing.BodyCondition = request.BodyCondition;
            await MarkRecalculationAsync("used", ct);
            listing.ManuallyMatched = true;
            CarListingMatcher.Match(listing, []);
            Audit("used-listing", id, "Identity", request.Reason, before,
                new { listing.Model, listing.Trim, listing.Year, listing.YearCalendar, listing.Mileage, listing.Transmission, listing.BodyCondition });
        }, ct);
        else if (scope == "zero") await zeroStore.ExclusiveAsync(async () => {
            var listing = await db.Set<ZeroVehicleListing>().SingleOrDefaultAsync(x => x.Id == id, ct); if (listing is null) return;
            found = true; var before = new { listing.ModelId, listing.TrimId, listing.Year, listing.YearCalendar };
            listing.ModelId = model.Id; listing.TrimId = trim.Id; listing.Year = request.Year; listing.YearCalendar = request.YearCalendar;
            listing.OriginalName = model.Name + (trim.Name == "نام‌محور" ? "" : " " + trim.Name); listing.ManuallyMatched = true;
            listing.PricingStatus = "Pending"; listing.RejectionReason = null;
            listing.MarketStatus = "NotFound"; listing.MarketPrice = null; listing.FinalPrice = null;
            await InvalidateZeroGroupsAsync(before.ModelId, before.TrimId, before.Year, before.YearCalendar, listing.PriceDay, ct);
            await MarkRecalculationAsync("zero", ct);
            Audit("zero-listing", id, "Identity", request.Reason, before, new { listing.ModelId, listing.TrimId, listing.Year, listing.YearCalendar });
        }, ct);
        else return BadRequest();
        if (!found) return NotFound();
        return Accepted(new { queued = true, workerEnabled = scope == "used" ? used.WorkerEnabled : zero.WorkerEnabled });
    }

    [HttpPost("listings/{scope}/{id:long}/decision")]
    public async Task<IActionResult> ListingDecision(string scope, long id, MarketDecisionRequest request, CancellationToken ct)
    {
        if (request.Decision is not ("Approved" or "Rejected" or "Unreviewed") || string.IsNullOrWhiteSpace(request.Reason)) return BadRequest();
        var found = false;
        if (scope == "used") await usedStore.RunExclusiveAsync("TelegramDailyPricing", async () => {
            var row = await db.CarListings.SingleOrDefaultAsync(x => x.Id == id && !x.IsDelete, ct); if (row is null) return;
            found = true; var before = row.AdminDecision; await InvalidateUsedAsync(row, ct); row.AdminDecision = request.Decision;
            if (request.Decision == "Rejected") row.ProcessingStatus = "NeedsReview";
            await MarkRecalculationAsync("used", ct);
            Audit("used-listing", id, "Decision", request.Reason, before, request.Decision);
        }, ct);
        else if (scope == "zero") await zeroStore.ExclusiveAsync(async () => {
            var row = await db.Set<ZeroVehicleListing>().SingleOrDefaultAsync(x => x.Id == id, ct); if (row is null) return;
            found = true; var before = row.AdminDecision; row.AdminDecision = request.Decision;
            await InvalidateZeroGroupsAsync(row.ModelId, row.TrimId, row.Year, row.YearCalendar, row.PriceDay, ct);
            await MarkRecalculationAsync("zero", ct);
            Audit("zero-listing", id, "Decision", request.Reason, before, request.Decision);
        }, ct);
        else return BadRequest();
        if (!found) return NotFound();
        return Accepted(new { queued = true, workerEnabled = scope == "used" ? used.WorkerEnabled : zero.WorkerEnabled });
    }

    private async Task MarkRecalculationAsync(string scope, CancellationToken ct)
    {
        if (scope == "used")
        {
            var state = await db.TelegramPricingStates.SingleOrDefaultAsync(ct);
            if (state is null) { state = new(); db.Add(state); }
            state.PricingRequestedAt = DateTime.UtcNow;
        }
        else
        {
            var state = await db.Set<ZeroVehicleSetting>().SingleOrDefaultAsync(ct);
            if (state is null) { state = new() { DeviationPercent = await zero.SettingsAsync(null, ct) }; db.Add(state); }
            state.RecalculationRequestedAt = DateTime.UtcNow;
        }
    }

    private async Task InvalidateZeroGroupsAsync(long modelId, long trimId, int? year, string? calendar, DateOnly? day, CancellationToken ct)
    {
        foreach (var group in await db.Set<ZeroVehiclePriceGroup>().Where(x => x.ModelId == modelId && x.TrimId == trimId
            && x.Year == year && x.YearCalendar == calendar && x.PriceDay == day).ToListAsync(ct)) group.PricingStatus = "Unavailable";
    }

    private async Task LockZeroAsync(CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource='ZeroVehicleProcessing', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51000, 'Processing is busy.', 1;", ct);
    }

    private async Task InvalidateUsedAsync(CarListing row, CancellationToken ct)
    {
        if (row.CarId.HasValue)
        {
            var car = await db.Cars.SingleOrDefaultAsync(x => x.Id == row.CarId, ct);
            if (car is not null && car.MarketPriceDay == row.PriceDay)
            { car.CalculatedMarketPrice = 0; car.MarketPrice = MarketPricePolicy.Effective(0, car.ManualOverridePrice, car.ManualOverrideUntil, car.PriceReviewStatus, DateTime.UtcNow); }
            var history = await db.CarPriceHistories.SingleOrDefaultAsync(x => x.CarId == row.CarId && x.PriceDay == row.PriceDay, ct);
            if (history is not null) { history.IsFinal = false; history.ConfidenceLevel = "NeedsReview"; history.SampleCount = history.IndependentSourceCount = 0; }
        }
    }

    private async Task QueueModelRecheckAsync(string modelName, CancellationToken ct, string? previousName, long modelId)
    {
        // Derived candidates only. Raw content and explicit administrator matches stay unchanged.
        var canonical = CarListingMatcher.Normalize(modelName);
        var previous = CarListingMatcher.Normalize(previousName ?? modelName);
        var rows = await db.CarListings.Where(x => !x.IsDelete && !x.ManuallyMatched && (x.Model == canonical || x.Model == previous || x.Model == null)).ToListAsync(ct);
        foreach (var row in rows) { await InvalidateUsedAsync(row, ct); row.ExtractionVersion = 0; row.StoredAt = null; row.ProcessingStatus = "Pending"; }
        var ids = rows.Where(x => x.RawMessageId.HasValue).Select(x => x.RawMessageId!.Value).ToArray();
        foreach (var chunk in ids.Chunk(1000))
            foreach (var raw in await db.TelegramRawMessages.Where(x => chunk.Contains(x.Id)).ToListAsync(ct)) raw.ExtractionVersion = 0;
        var zeroCatalog = await db.Set<ZeroVehicleModel>().AsNoTracking().Include(x => x.Trims).ToListAsync(ct);
        var options = CarShow.ApplicationService.Services.ZeroVehicles.ZeroVehicleOptions.Read(
            HttpContext.RequestServices.GetRequiredService<IConfiguration>());
        foreach (var row in await db.Set<ZeroVehicleListing>().Where(x => x.ModelId == modelId && !x.ManuallyMatched).ToListAsync(ct))
        {
            var parsed = CarShow.ApplicationService.Services.ZeroVehicles.ZeroVehicleRules.Validate(
                new(row.ExternalMessageId, row.RawText, row.PublishedAt, row.SourceUrl, null), row.Channel, zeroCatalog, options, DateTime.UtcNow);
            if (parsed.Listing is { } updated)
            {
                row.ModelId = updated.ModelId; row.TrimId = updated.TrimId; row.OriginalName = updated.OriginalName;
                row.Year = updated.Year; row.YearCalendar = updated.YearCalendar; row.PriceDay = updated.PriceDay;
                row.PricingStatus = "Pending"; row.MarketStatus = "NotFound";
            }
            else { row.PricingStatus = "IdentityNeedsReview"; row.MarketStatus = "NotFound"; row.RejectionReason = parsed.RejectionCode; }
            row.FinalPrice = null;
        }
        foreach (var group in await db.Set<ZeroVehiclePriceGroup>().Where(x => x.ModelId == modelId).ToListAsync(ct)) group.PricingStatus = "Unavailable";
        var zeroState = await db.Set<ZeroVehicleSetting>().SingleOrDefaultAsync(ct);
        if (zeroState is null) { zeroState = new() { DeviationPercent = options.DeviationPercent }; db.Add(zeroState); }
        zeroState.RecalculationRequestedAt = DateTime.UtcNow;

    }
}
