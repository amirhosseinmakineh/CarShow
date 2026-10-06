using CarShow.Domain.Crawling;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CarCrawlerController : ControllerBase
{
    private readonly ICarCrawlerSyncService _synchronizer;

    public CarCrawlerController(ICarCrawlerSyncService synchronizer)
    {
        _synchronizer = synchronizer;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var count = await _synchronizer.SyncAsync(cancellationToken);
        return Ok(new
        {
            success = true,
            processed = count,
            message = "Car.ir data synchronization completed."
        });
    }
}
