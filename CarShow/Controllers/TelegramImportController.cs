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
    private readonly TelegramRejectionDiagnosticsWorker _diagnosticsWorker;

    public TelegramImportController(TelegramScanService scan, TelegramScanStatus status, TelegramScanBackgroundService worker, TelegramRejectionDiagnosticsWorker diagnosticsWorker)
    {
        _scan = scan;
        _status = status;
        _worker = worker;
        _diagnosticsWorker = diagnosticsWorker;
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

    [HttpGet("diagnostics/rejections")]
    public IActionResult GetRejectionDiagnostics()
        => Ok(new
        {
            message = "Diagnostics now runs in the background. Start with POST /api/telegram-cars/diagnostics/rejections, then poll status and results.",
            statusUrl = "/api/telegram-cars/diagnostics/rejections/status",
            resultsUrl = "/api/telegram-cars/diagnostics/rejections/results"
        });

    [HttpPost("diagnostics/rejections")]
    public IActionResult QueueRejectionDiagnostics()
    {
        if (!_diagnosticsWorker.TryQueue())
            return Conflict(new { message = "Diagnostics is already queued or running.",
                statusUrl = "/api/telegram-cars/diagnostics/rejections/status" });
        return Accepted(new { message = "Diagnostics queued.",
            statusUrl = "/api/telegram-cars/diagnostics/rejections/status",
            resultsUrl = "/api/telegram-cars/diagnostics/rejections/results" });
    }

    [HttpGet("diagnostics/rejections/status")]
    public IActionResult GetRejectionDiagnosticsStatus()
        => Ok(_diagnosticsWorker.ReadStatus());

    [HttpGet("diagnostics/rejections/results")]
    public IActionResult GetRejectionDiagnosticsResults()
        => Ok(_diagnosticsWorker.ReadResults());

    [HttpPost("diagnostics/parsed-cars")]
    public IActionResult DiagnoseParsedCars([FromBody] List<TelegramScanService.ParsedCar> cars)
    {
        var outcome = _scan.DiagnoseParsedCars(cars);
        return StatusCode(outcome.StatusCode, outcome.Value);
    }
}
