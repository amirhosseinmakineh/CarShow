using CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CarShow.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/zero-vehicles")]
public sealed class ZeroVehiclesController(ZeroVehicleService service) : ControllerBase
{
    [HttpPost("imports")]
    public async Task<IActionResult> Import(ZeroImportRequest request, CancellationToken ct)
    {
        try
        {
            var run = await service.EnqueueAsync(request, ct);
            return AcceptedAtAction(nameof(ImportResult), new { id = run.Id }, run);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (TelegramProcessingBusyException) { return Conflict(new { message = "پردازش دیگری فعال است؛ چند لحظه دیگر تلاش کنید." }); }
    }
    [HttpGet("imports/{id:long}")]
    public async Task<IActionResult> ImportResult(long id, CancellationToken ct) => await service.GetRunAsync(id, ct) is { } run ? Ok(run) : NotFound();
    [HttpGet("imports")]
    public async Task<IActionResult> Imports(CancellationToken ct, int page = 0, int pageSize = 10) => Ok(await service.RunsAsync(page, pageSize, ct));
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await service.CatalogAsync(ct));
    [HttpGet("listings")]
    public async Task<IActionResult> Listings(CancellationToken ct, string? search = null, long? modelId = null, long? trimId = null, string? status = null, int page = 0, int pageSize = 10)
        => Ok(await service.ListingsAsync(search, modelId, trimId, status, page, pageSize, ct));
    [HttpGet("listings/{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct) => await service.DetailAsync(id, ct) is { } detail ? Ok(detail) : NotFound();
    [HttpGet("prices")]
    public async Task<IActionResult> Prices(CancellationToken ct, string? search = null, int page = 0, int pageSize = 10) => Ok(await service.PricesAsync(search, page, pageSize, ct));
    [HttpGet("prices/{id:long}/listings")]
    public async Task<IActionResult> Members(long id, CancellationToken ct, int page = 0, int pageSize = 10) => await service.MembersAsync(id, page, pageSize, ct) is { } pageResult ? Ok(pageResult) : NotFound();
    [HttpPost("prices/recalculate")]
    public async Task<IActionResult> Recalculate(CancellationToken ct)
    {
        try { return Ok(new { groups = await service.RecalculateAsync(DateTime.UtcNow, ct) }); }
        catch (TelegramProcessingBusyException) { return Conflict(new { message = "پردازش دیگری فعال است؛ چند لحظه دیگر تلاش کنید." }); }
    }
}
