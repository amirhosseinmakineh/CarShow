using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/admin/telegram-channels/preview")]
[Authorize(Roles = "Admin")]
public sealed class TelegramPreviewController(TelegramPreviewService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Preview(TelegramPreviewRequest request, CancellationToken token)
        => Ok(await service.CollectAsync(request, token));
}
