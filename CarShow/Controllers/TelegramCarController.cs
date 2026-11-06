using CarShow.Infrastracture.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/telegram-cars")]
[Authorize(Roles = "Admin")]
public sealed class TelegramCarController(TelegramCarService scanner) : ControllerBase
{
    /// <summary>Reads approved public Telegram pages and returns extracted name, year and price in toman. No database writes.</summary>
    [HttpGet("scan")]
    public async Task<ActionResult<TelegramScanResult>> Scan(
        [FromQuery] string? channel = null,
        [FromQuery] int maxMessagesPerChannel = 20,
        CancellationToken cancellationToken = default)
    {
        if (maxMessagesPerChannel is < 1 or > 500)
            return BadRequest("maxMessagesPerChannel must be between 1 and 500.");
        if (channel is not null && !TelegramCarService.Channels.Contains(channel.Trim().TrimStart('@'), StringComparer.OrdinalIgnoreCase))
            return BadRequest("Only approved public channel usernames can be scanned. Private invites require Telegram access.");
        return Ok(await scanner.GetCarsAsync(maxMessagesPerChannel, channel, cancellationToken));
    }
}
