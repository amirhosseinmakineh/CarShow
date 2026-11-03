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
    [HttpGet("channels")]
    public async Task<IActionResult> Channels(CancellationToken ct) => Ok(await service.ChannelsAsync(ct));
    [HttpPost("channels")]
    public async Task<IActionResult> AddChannels(ZeroChannelsRequest request, CancellationToken ct)
    {
        try { return Ok(await service.AddChannelsAsync(request.Identifiers, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpPatch("channels/{id:long}")]
    public async Task<IActionResult> ToggleChannel(long id, ZeroChannelToggle request, CancellationToken ct)
        => await service.ChangeChannelAsync(id, request.Enabled, ct) ? NoContent() : NotFound();
    [HttpDelete("channels/{id:long}")]
    public async Task<IActionResult> DeleteChannel(long id, CancellationToken ct)
        => await service.ChangeChannelAsync(id, null, ct) ? NoContent() : NotFound();
    [HttpPost("imports/all")]
    public async Task<IActionResult> ImportAll(ZeroAllImportRequest request, CancellationToken ct)
        => Accepted(await service.ImportAllAsync(request.MessageLimit, ct));
    [HttpGet("imports/{id:long}/issues")]
    public async Task<IActionResult> Issues(long id, CancellationToken ct, int page = 0) => Ok(await service.IssuesAsync(id, page, ct));
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(new { deviationPercent = await service.SettingsAsync(null, ct) });
    [HttpPut("settings")]
    public async Task<IActionResult> Settings(ZeroSettingsRequest request, CancellationToken ct)
        => Ok(new { deviationPercent = await service.SettingsAsync(request.DeviationPercent, ct) });
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
