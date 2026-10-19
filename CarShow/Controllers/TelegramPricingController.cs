using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Contract.Dtos.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/admin/telegram-pricing")]
[Authorize(Roles = "Admin")]
public sealed class TelegramPricingController(ITelegramPricingQueries queries) : ControllerBase
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

    [HttpGet("prices")]
    public async Task<IActionResult> Prices(long? carId, int page, CancellationToken cancellationToken)
    {
        return Ok(await queries.PricesAsync(carId, page, cancellationToken));
    }
}
