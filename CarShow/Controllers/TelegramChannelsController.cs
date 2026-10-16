using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/admin/telegram-channels")]
[Authorize(Roles = "Admin")]
public sealed class TelegramChannelsController(ITelegramChannelService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(await service.GetChannelsAsync(cancellationToken));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        return Ok(await service.GetSummaryAsync(cancellationToken));
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(CancellationToken cancellationToken)
    {
        return Ok(await service.GetJobsAsync(cancellationToken));
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(
        BulkTelegramChannelImportRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await service.ImportAsync(request, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Add(
        AddTelegramChannelRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await service.AddAsync(request, cancellationToken));
    }

    [HttpPatch("{id:long}/enabled")]
    public async Task<IActionResult> Enabled(
        long id,
        [FromBody] bool enabled,
        CancellationToken cancellationToken)
    {
        var updated = await service.SetEnabledAsync(id, enabled, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:long}/retry")]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken)
    {
        var queued = await service.RetryAsync(id, cancellationToken);
        return queued ? Accepted() : NotFound();
    }

    [HttpPost("{id:long}/test")]
    public async Task<IActionResult> Test(long id, CancellationToken cancellationToken)
    {
        var queued = await service.TestAsync(id, cancellationToken);
        return queued ? Accepted() : NotFound();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
