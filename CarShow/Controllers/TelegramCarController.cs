using System.Collections.Concurrent;
using CarShow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/telegram-cars")]
[Authorize(Roles = "Admin")]
public sealed class TelegramCarController(
    TelegramCarService scanner,
    TelegramCarScanJobManager jobs,
    ILogger<TelegramCarController> logger) : ControllerBase
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

        if (channel is null && maxMessagesPerChannel > 20)
            return BadRequest("For more than 20 messages per channel, start a scan job with POST /api/telegram-cars/scan-jobs and poll its status.");

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

    [HttpPost("scan-jobs")]
    public ActionResult StartScanJob(
        [FromQuery] int maxMessagesPerChannel = 100,
        [FromQuery] string? channel = null)
    {
        if (maxMessagesPerChannel is < 1 or > 500)
            return BadRequest("maxMessagesPerChannel must be between 1 and 500.");

        if (channel is not null &&
            !TelegramCarService.ApprovedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
            return BadRequest("channel must be an approved public Telegram username.");

        var (id, started) = jobs.Start(maxMessagesPerChannel, channel);
        return started
            ? AcceptedAtAction(nameof(GetScanJob), new { id }, new { id, status = "Running" })
            : Conflict(new { id, message = "A scan is already running. Poll this job instead." });
    }

    // Admin-only diagnostic: return a few real, unmodified Telegram texts so
    // a NoMatches channel can be diagnosed without guessing its price format.
    // It never writes to the database or publishes the messages publicly.
    [HttpGet("scan-preview")]
    public async Task<ActionResult> ScanPreview(
        [FromQuery] string channel,
        [FromQuery] int maxMessages = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channel) ||
            !TelegramCarService.ApprovedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
            return BadRequest("channel must be an approved public Telegram username.");

        if (maxMessages is < 1 or > 10)
            return BadRequest("maxMessages must be between 1 and 10.");

        var samples = new List<TelegramMessageSample>();
        var diagnostics = new List<string>();
        var cars = await scanner.GetCarsAsync(maxMessages,
            diagnostics.Add, cancellationToken, channel,
            sample => samples.Add(sample));

        return Ok(new
        {
            Channel = channel,
            Messages = samples,
            Cars = cars,
            Diagnostics = diagnostics
        });
    }

    [HttpGet("scan-jobs/{id:guid}")]
    public ActionResult<TelegramScanStatus> GetScanJob(Guid id)
    {
        var status = jobs.GetStatus(id);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpGet("scan-jobs/{id:guid}/cars")]
    public ActionResult<List<TelegramCar>> GetScanJobCars(Guid id)
    {
        var status = jobs.GetStatus(id);
        if (status is null)
            return NotFound();
        if (status.Status == "Running")
            return Conflict("The scan is still running. Poll /scan-jobs/{id} for progress.");
        return Ok(jobs.GetCars(id));
    }
}
