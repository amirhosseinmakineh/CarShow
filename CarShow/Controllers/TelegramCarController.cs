using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/telegram-cars")]
[Authorize(Roles = "Admin")]
public sealed class TelegramCarController(TelegramCarService scanner, ILogger<TelegramCarController> logger) : ControllerBase
{
    [HttpGet("scan")]
    public async Task<ActionResult<List<TelegramCar>>> Scan(
        [FromQuery] int maxMessagesPerChannel = 500,
        CancellationToken cancellationToken = default)
    {
        if (maxMessagesPerChannel <= 0)
            return BadRequest("maxMessagesPerChannel must be positive.");

        var cars = await scanner.GetCarsAsync(
            maxMessagesPerChannel,
            message => logger.LogInformation("Telegram car scan: {Message}", message),
            cancellationToken);
        return Ok(cars);
    }
}
