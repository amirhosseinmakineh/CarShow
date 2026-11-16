using CarShow.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/telegram-cars")]
[AllowAnonymous]
public sealed class TelegramImportController : ControllerBase
{
    private readonly TelegramScanService _scan;
    private readonly TelegramScanStatus _status;
    private readonly TelegramScanBackgroundService _worker;

    public TelegramImportController(TelegramScanService scan, TelegramScanStatus status, TelegramScanBackgroundService worker)
    {
        _scan = scan;
        _status = status;
        _worker = worker;
    }

    [HttpPost("scan")]
    public async Task<IActionResult> RunScan(CancellationToken cancellationToken)
    {
        if (!_worker.TryQueueManualScan())
            return Conflict(new { message = "A Telegram scan is already queued or running." });
        return Accepted(new { message = "Scan queued.", statusUrl = "/api/telegram-cars/scan/status", resultsUrl = "/api/telegram-cars/scan/results" });
    }

    [HttpGet("scan/results")]
    public async Task<IActionResult> GetScanResults(CancellationToken cancellationToken)
        => Ok(await _status.ReadResultsAsync(cancellationToken));

    [HttpGet("scan/status")]
    public async Task<IActionResult> GetScanStatus(CancellationToken cancellationToken)
        => Ok(await _status.ReadAsync(cancellationToken));

    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics(CancellationToken cancellationToken)
    {
        var outcome = await _scan.GetDiagnostics(cancellationToken);
        return StatusCode(outcome.StatusCode, outcome.Value);
    }

    [HttpPost("diagnostics/parsed-cars")]
    public IActionResult DiagnoseParsedCars([FromBody] List<TelegramScanService.ParsedCar> cars)
    {
        var outcome = _scan.DiagnoseParsedCars(cars);
        return StatusCode(outcome.StatusCode, outcome.Value);
    }
}
