using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using CarShow.ApplicationService.Services;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Infrastracture.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Controllers;

public sealed record MarketOverrideRequest(decimal? Price, DateTime? ValidUntil, [Required, MaxLength(1024)] string Reason);
public sealed record MarketDecisionRequest([Required] string Decision, [Required, MaxLength(1024)] string Reason);
public sealed record MarketIdentityRequest(long ModelId, long? TrimId, int? Year, string? YearCalendar,
    int? Mileage, string? Transmission, string? BodyCondition, [Required, MaxLength(1024)] string Reason);

[TypeFilter(typeof(MarketReviewExceptionFilter))]
[ApiController, Authorize(Roles = "Admin"), Route("api/admin/market-review")]
public sealed partial class MarketReviewController(CarShowContext db, ITelegramProcessingStore usedStore,
    IZeroVehicleStore zeroStore, TelegramPricingService used, ZeroVehicleService zero) : ControllerBase
{
    [HttpGet("prices/{scope}")]
    public async Task<IActionResult> Prices(string scope, CancellationToken ct, string? search = null, int? year = null,
        int minSources = 0, int page = 0, string? trim = null, decimal? minPrice = null, decimal? maxPrice = null,
        string? reviewStatus = null, string? pricingStatus = null, DateOnly? from = null, DateOnly? to = null, string? mileageBucket = null)
    {
        page = Math.Clamp(page, 0, 100000);
        if (minPrice < 0 || maxPrice < 0 || maxPrice < minPrice || from > to || minSources < 0) return BadRequest(new { message = "بازه فیلتر معتبر نیست." });
        if (mileageBucket is not (null or "" or "unknown" or "1-50000" or "50001-100000" or "100001-200000" or "200001-300000" or "300001+")) return BadRequest();
        if (scope == "used")
        {
            var query = db.Cars.AsNoTracking().Where(x => !x.IsDelete && x.IsUsed == true && x.TelegramIdentityKey != null);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = CarListingMatcher.Normalize(search);
                query = query.Where(x => x.Name.Contains(search) || x.Name.Contains(term) || x.TelegramIdentityKey!.Contains(term)
                    || x.Company != null && x.Company.Name.Contains(search));
            }
            if (year.HasValue) query = query.Where(x => x.Year == year);
            if (trim == "UnknownTrim") query = query.Where(x => x.Tip == null);
            else if (!string.IsNullOrWhiteSpace(trim)) query = query.Where(x => x.Tip != null && x.Tip.Name == trim);
            if (minPrice.HasValue) query = query.Where(x => x.MarketPrice >= minPrice);
            if (maxPrice.HasValue) query = query.Where(x => x.MarketPrice <= maxPrice);
            if (!string.IsNullOrWhiteSpace(reviewStatus)) query = query.Where(x => x.PriceReviewStatus == reviewStatus);
            if (pricingStatus == "SingleSource") query = query.Where(x => x.PriceSampleCount == 1);
            if (pricingStatus == "MultipleSources") query = query.Where(x => x.PriceSampleCount > 1);
            if (from.HasValue) query = query.Where(x => x.MarketPriceDay >= from);
            if (to.HasValue) query = query.Where(x => x.MarketPriceDay <= to);
            if (!string.IsNullOrWhiteSpace(mileageBucket)) query = query.Where(x => x.TelegramIdentityKey!.Contains("|" + mileageBucket));
            query = query.Where(x => x.PriceSampleCount >= minSources);
            return Ok(new { total = await query.CountAsync(ct), page, pageSize = 20,
                items = await query.OrderByDescending(x => x.MarketPriceDay).ThenBy(x => x.Id).Skip(page * 20).Take(20)
                    .Select(x => new { x.Id, x.Name, model = x.CarModel.Name, trim = x.Tip != null ? x.Tip.Name : null,
                        x.Year, x.YearCalendar, priceDay = x.MarketPriceDay, calculatedMarketPrice = x.CalculatedMarketPrice ?? x.MarketPrice,
                        x.ManualOverridePrice, x.ManualOverrideUntil, effectivePrice = x.MarketPrice, x.PriceReviewStatus,
                        sourceCount = x.PriceSampleCount, confidence = x.PriceSampleCount == 1 ? "Low" : "MultipleSources",
                        pricingStatus = x.PriceSampleCount == 1 ? "SingleSource" : "Calculated", calculatedAt = x.PriceCalculatedAt }).ToListAsync(ct) });
        }
        if (scope != "zero") return BadRequest();
        var prices = db.Set<ZeroVehiclePriceGroup>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) prices = prices.Where(x => x.Model.Name.Contains(search) || x.Trim.Name.Contains(search));
        if (year.HasValue) prices = prices.Where(x => x.Year == year);
        if (trim == "UnknownTrim") prices = prices.Where(x => x.Trim.Name == "نام‌محور");
        else if (!string.IsNullOrWhiteSpace(trim)) prices = prices.Where(x => x.Trim.Name == trim);
        if (minPrice.HasValue) prices = prices.Where(x => x.ReferencePrice >= minPrice);
        if (maxPrice.HasValue) prices = prices.Where(x => x.ReferencePrice <= maxPrice);
        if (!string.IsNullOrWhiteSpace(reviewStatus)) prices = prices.Where(x => x.PriceReviewStatus == reviewStatus);
        if (!string.IsNullOrWhiteSpace(pricingStatus)) prices = pricingStatus switch {
            "SingleSource" => prices.Where(x => x.SourceCount == 1), "MultipleSources" => prices.Where(x => x.SourceCount > 1),
            _ => prices.Where(x => x.PricingStatus == pricingStatus) };
        if (from.HasValue) prices = prices.Where(x => x.PriceDay >= from);
        if (to.HasValue) prices = prices.Where(x => x.PriceDay <= to);
        prices = prices.Where(x => x.SourceCount >= minSources);
        return Ok(new { total = await prices.CountAsync(ct), page, pageSize = 20,
            items = await prices.OrderByDescending(x => x.PriceDay).ThenBy(x => x.Id).Skip(page * 20).Take(20)
                .Select(x => new { x.Id, name = x.Model.Name + " " + x.Trim.Name, model = x.Model.Name, trim = x.Trim.Name,
                    x.Year, x.YearCalendar, x.PriceDay, calculatedMarketPrice = x.CalculatedMarketPrice ?? x.ReferencePrice,
                    x.ManualOverridePrice, x.ManualOverrideUntil, effectivePrice = x.ReferencePrice, x.PriceReviewStatus,
                    x.SourceCount, x.Confidence, x.PricingStatus, x.CalculatedAt }).ToListAsync(ct) });
    }

    [HttpPost("prices/{scope}/{id:long}/override")]
    public async Task<IActionResult> Override(string scope, long id, MarketOverrideRequest request, CancellationToken ct)
    {
        if (request.Price is <= 0 or > 100_000_000_000m || string.IsNullOrWhiteSpace(request.Reason)
            || request.ValidUntil is { } until && (until.Kind == DateTimeKind.Unspecified || until <= DateTime.UtcNow))
            return BadRequest(new { message = "قیمت مثبت، دلیل تغییر و تاریخ اعتبار آینده با منطقه زمانی مشخص وارد کنید." });
        var found = false;
        if (scope == "used") await usedStore.RunExclusiveAsync("TelegramDailyPricing", async () => {
            var car = await db.Cars.SingleOrDefaultAsync(x => x.Id == id && !x.IsDelete && x.IsUsed == true, ct);
            if (car is null) return;
            found = true;
            var before = new { car.MarketPrice, car.CalculatedMarketPrice, car.ManualOverridePrice, car.ManualOverrideUntil };
            car.CalculatedMarketPrice ??= car.MarketPrice;
            car.ManualOverridePrice = request.Price; car.ManualOverrideUntil = request.ValidUntil?.ToUniversalTime();
            car.MarketPrice = MarketPricePolicy.Effective(car.CalculatedMarketPrice.Value, request.Price,
                car.ManualOverrideUntil, car.PriceReviewStatus, DateTime.UtcNow);
            Audit("used-price", id, request.Price.HasValue ? "Override" : "RevokeOverride", request.Reason, before,
                new { car.MarketPrice, car.ManualOverridePrice, car.ManualOverrideUntil });
        }, ct);
        else if (scope == "zero") await zeroStore.ExclusiveAsync(async () => {
            var price = await db.Set<ZeroVehiclePriceGroup>().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (price is null) return;
            found = true;
            var before = new { price.ReferencePrice, price.CalculatedMarketPrice, price.ManualOverridePrice, price.ManualOverrideUntil };
            price.CalculatedMarketPrice ??= price.ReferencePrice;
            price.ManualOverridePrice = request.Price; price.ManualOverrideUntil = request.ValidUntil?.ToUniversalTime();
            price.ReferencePrice = MarketPricePolicy.Effective(price.CalculatedMarketPrice.Value, request.Price,
                price.ManualOverrideUntil, price.PriceReviewStatus, DateTime.UtcNow);
            Audit("zero-price", id, request.Price.HasValue ? "Override" : "RevokeOverride", request.Reason, before,
                new { price.ReferencePrice, price.ManualOverridePrice, price.ManualOverrideUntil });
        }, ct);
        else return BadRequest();
        return found ? NoContent() : NotFound();
    }

    [HttpPost("prices/{scope}/{id:long}/decision")]
    public async Task<IActionResult> PriceDecision(string scope, long id, MarketDecisionRequest request, CancellationToken ct)
    {
        if (request.Decision is not ("Approved" or "Rejected" or "Unreviewed") || string.IsNullOrWhiteSpace(request.Reason)) return BadRequest();
        var found = false;
        if (scope == "used") await usedStore.RunExclusiveAsync("TelegramDailyPricing", async () => {
            var car = await db.Cars.SingleOrDefaultAsync(x => x.Id == id && x.IsUsed == true, ct); if (car is null) return;
            found = true; var before = car.PriceReviewStatus;
            car.CalculatedMarketPrice ??= car.MarketPrice;
            car.PriceReviewStatus = request.Decision;
            car.MarketPrice = MarketPricePolicy.Effective(car.CalculatedMarketPrice.Value, car.ManualOverridePrice,
                car.ManualOverrideUntil, car.PriceReviewStatus, DateTime.UtcNow);
            Audit("used-price", id, "Decision", request.Reason, before, request.Decision);
        }, ct);
        else if (scope == "zero") await zeroStore.ExclusiveAsync(async () => {
            var group = await db.Set<ZeroVehiclePriceGroup>().SingleOrDefaultAsync(x => x.Id == id, ct); if (group is null) return;
            found = true; var before = group.PriceReviewStatus;
            group.CalculatedMarketPrice ??= group.ReferencePrice; group.PriceReviewStatus = request.Decision;
            group.ReferencePrice = MarketPricePolicy.Effective(group.CalculatedMarketPrice.Value, group.ManualOverridePrice,
                group.ManualOverrideUntil, group.PriceReviewStatus, DateTime.UtcNow);
            Audit("zero-price", id, "Decision", request.Reason, before, request.Decision);
        }, ct);
        else return BadRequest();
        return found ? NoContent() : NotFound();
    }

    [HttpGet("prices/{scope}/{id:long}/sources")]
    public async Task<IActionResult> Sources(string scope, long id, CancellationToken ct, int page = 0)
    {
        if (scope == "used")
        {
            var car = await db.Cars.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (car is null) return NotFound();
            var rows = db.CarListings.AsNoTracking().Where(x => x.CarId == id && x.PriceDay == car.MarketPriceDay);
            return Ok(new { total = await rows.CountAsync(ct), page = Math.Clamp(page, 0, 100000), pageSize = 50,
                items = await rows.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Skip(Math.Clamp(page, 0, 100000) * 50).Take(50).Select(x => new { x.Id, x.RawCarName, x.Model, x.Trim, x.Year, x.YearCalendar,
                    x.Mileage, x.Transmission, x.BodyCondition, extractedPrice = x.Price, x.NormalizedPrice, x.Currency,
                    x.SourceChannel, x.SourceUrl, x.PublishedAt, x.ComparisonStatus, x.ReasonCode, x.AdminDecision }).ToListAsync(ct) });
        }
        if (scope != "zero") return BadRequest();
        var price = await db.Set<ZeroVehiclePriceGroup>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (price is null) return NotFound();
        var zeroRows = db.Set<ZeroVehicleListing>().AsNoTracking().Where(x => x.ModelId == price.ModelId && x.TrimId == price.TrimId
            && x.Year == price.Year && x.YearCalendar == price.YearCalendar && x.PriceDay == price.PriceDay);
        return Ok(new { total = await zeroRows.CountAsync(ct), page = Math.Clamp(page, 0, 100000), pageSize = 50,
            items = await zeroRows.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Skip(Math.Clamp(page, 0, 100000) * 50).Take(50).Select(x => new { x.Id, rawCarName = x.OriginalName, model = x.Model.Name,
                trim = x.Trim.Name, x.Year, x.YearCalendar, extractedPrice = x.PriceToman, currency = "IRT", sourceChannel = x.Channel,
                x.SourceUrl, x.PublishedAt, comparisonStatus = x.PricingStatus, reasonCode = x.RejectionReason, x.AdminDecision }).ToListAsync(ct) });
    }

    [HttpGet("audit/{scope}/{id:long}")]
    public async Task<IActionResult> History(string scope, long id, CancellationToken ct, int page = 0)
        => Ok(await db.Set<MarketPriceReview>().AsNoTracking().Where(x => x.Scope == scope && x.TargetId == id)
            .OrderByDescending(x => x.Id).Skip(Math.Clamp(page, 0, 100000) * 20).Take(20).ToListAsync(ct));

    [HttpPost("recalculate/{scope}")]
    public async Task<IActionResult> Recalculate(string scope, CancellationToken ct)
    {
        if (scope == "used") await used.RequestRecalculationAsync(ct);
        else if (scope == "zero") await zero.RequestRecalculationAsync(ct);
        else return BadRequest();
        return Accepted(new { queued = true, workerEnabled = scope == "used" ? used.WorkerEnabled : zero.WorkerEnabled, message = "درخواست محاسبه در بک‌اند ثبت شد." });
    }

    private void Audit(string scope, long id, string action, string reason, object? before, object? after)
        => db.Add(new MarketPriceReview { Scope = scope, TargetId = id, Action = action,
            Actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.Identity?.Name ?? "authenticated-admin",
            Reason = reason, BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after) });
}
