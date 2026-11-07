using System.Collections.Concurrent;
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
        [FromQuery] int maxMessagesPerChannel = 20,
        [FromQuery] string? channel = null,
        CancellationToken cancellationToken = default)
    {
        if (maxMessagesPerChannel is < 1 or > 500)
            return BadRequest("maxMessagesPerChannel must be between 1 and 500.");

        if (channel is not null &&
            !TelegramCarService.ApprovedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
            return BadRequest("channel must be an approved public Telegram username.");

        var cars = new ConcurrentBag<TelegramCar>();
        var failures = new ConcurrentBag<string>();
        IReadOnlyList<string> channels = channel is null
            ? TelegramCarService.ApprovedChannels
            : [channel];

        // Each channel keeps its own pagination state. Bounded concurrency prevents
        // a full scan from holding the HTTP response for 84 sequential requests.
        await Parallel.ForEachAsync(channels,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 12,
                CancellationToken = cancellationToken
            },
            async (username, token) =>
            {
                var results = await scanner.GetCarsAsync(
                    maxMessagesPerChannel,
                    message =>
                    {
                        logger.LogInformation("Telegram car scan: {Message}", message);
                        if (!message.EndsWith(" messages processed.", StringComparison.Ordinal))
                            failures.Add(message);
                    },
                    token,
                    username);

                foreach (var car in results)
                    cars.Add(car);
            });

        if (cars.IsEmpty && failures.Count == channels.Count)
            return Problem("No public Telegram channel could be read. Check server logs for each channel's HTTP or access error.",
                statusCode: StatusCodes.Status502BadGateway);

        return Ok(cars.ToList());
    }
}
