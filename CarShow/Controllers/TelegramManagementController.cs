using CarShow.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

/// <summary>Administrative Telegram scan operations. Does not affect the catalog crawler.</summary>
[ApiController]
[Route("api/admin/telegram")]
[Authorize(Roles = "Admin")]
public sealed class TelegramManagementController : ControllerBase
{
    private readonly TelegramScanBackgroundService _worker;
    private readonly TelegramScanStatus _status;

    public TelegramManagementController(TelegramScanBackgroundService worker, TelegramScanStatus status)
    {
        _worker = worker;
        _status = status;
    }

    [HttpPost("scan")]
    public IActionResult StartScan()
    {
        if (!_worker.TryQueueManualScan())
            return Conflict(new { message = "A Telegram scan is already queued or running." });

        return Accepted(new { status = "Queued", message = "Telegram scan queued successfully." });
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        await _status.EnsureSchemaAsync(cancellationToken);
        return Ok(await _status.ReadAsync(cancellationToken));
    }

    [HttpGet("results")]
    public async Task<IActionResult> GetResults(CancellationToken cancellationToken)
    {
        await _status.EnsureSchemaAsync(cancellationToken);
        return Ok(await _status.ReadResultsAsync(cancellationToken));
    }
}
