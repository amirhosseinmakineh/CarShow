using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Services;
using CarShow.Domain.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/admin/telegram-pricing")]
[Authorize(Roles = "Admin")]
public sealed class TelegramPricingController(ITelegramPricingQueries queries, TelegramListingManagementService management) : ControllerBase
{
    [HttpGet("state")]
    public async Task<IActionResult> State(CancellationToken cancellationToken)
    {
        return Ok(await queries.StateAsync(cancellationToken) ?? new TelegramPricingStateDto(null, null, null));
    }

    [HttpGet("observations")]
    public async Task<IActionResult> Observations(string? status, int page, CancellationToken cancellationToken)
    {
        return Ok(await queries.ObservationsAsync(status, page, cancellationToken));
    }

    [HttpGet("jobs/{jobId:long}/timeline")]
    public async Task<IActionResult> Timeline(long jobId, CancellationToken cancellationToken)
    {
        var timeline = await queries.TimelineAsync(jobId, cancellationToken);
        return timeline is null ? NotFound() : Ok(timeline);
    }

    [HttpGet("pipeline/timeline")]
    public async Task<IActionResult> PipelineTimeline(CancellationToken cancellationToken)
    {
        return Ok(await queries.PipelineTimelineAsync(cancellationToken));
    }

    [HttpDelete("observations")]
    public async Task<IActionResult> ClearListings(
        [FromBody] TelegramListingClearRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedCount < 0)
        {
            return BadRequest(new { message = "تعداد آگهی‌های مورد انتظار معتبر نیست." });
        }
        TelegramListingClearResult? result;
        try
        {
            result = await management.ClearAsync(request.ExpectedCount, cancellationToken);
        }
        catch (TelegramProcessingBusyException)
        {
            return Conflict(new { message = "پردازش در حال اجراست. چند لحظه دیگر دوباره تلاش کنید." });
        }
        catch (Exception exception) when (SqlServerCapacityError.IsTransactionLogFull(exception))
        {
            return StatusCode(507, new { message = "لاگ تراکنش دیتابیس پر است و حذف آگهی‌ها ذخیره نشد. مدیر سرویس باید بکاپ لاگ و فضای ذخیره‌سازی را بررسی کند." });
        }
        return result is null
            ? Conflict(new { message = "تعداد آگهی‌ها تغییر کرده است. فهرست را دوباره بارگذاری کنید." })
            : Ok(result);
    }

    [HttpGet("prices")]
    public async Task<IActionResult> Prices(long? carId, int page, CancellationToken cancellationToken)
    {
        return Ok(await queries.PricesAsync(carId, page, cancellationToken));
    }
}
