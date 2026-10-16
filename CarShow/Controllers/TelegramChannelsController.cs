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
    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Ok(await service.GetChannelsAsync(ct));
    [HttpGet("summary")] public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await service.GetSummaryAsync(ct));
    [HttpPost("import")] public async Task<IActionResult> Import(BulkTelegramChannelImportRequest request, CancellationToken ct) => Ok(await service.ImportAsync(request, ct));
    [HttpPost] public async Task<IActionResult> Add(AddTelegramChannelRequest request, CancellationToken ct) => Ok(await service.AddAsync(request, ct));
    [HttpPatch("{id:long}/enabled")] public async Task<IActionResult> Enabled(long id, [FromBody] bool enabled, CancellationToken ct) => await service.SetEnabledAsync(id, enabled, ct) ? NoContent() : NotFound();
    [HttpPost("{id:long}/retry")] public async Task<IActionResult> Retry(long id, CancellationToken ct) => await service.RetryAsync(id, ct) ? Accepted() : NotFound();
    [HttpPost("{id:long}/test")] public async Task<IActionResult> Test(long id, CancellationToken ct) => await service.TestAsync(id, ct) ? Accepted() : NotFound();
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) => await service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
