using CarShow.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/telegram-cars")]
[Authorize(Roles = "Admin")]
public sealed class TelegramImportController : ControllerBase
{
    private readonly TelegramScanService _scan;
    private readonly TelegramScanStatus _status;

    public TelegramImportController(TelegramScanService scan, TelegramScanStatus status)
    {
        _scan = scan;
        _status = status;
    }

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
