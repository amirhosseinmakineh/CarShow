using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/admin/catalog")]
[Authorize(Roles = "Admin")]
public sealed class CatalogImportController(ICarSErvicce carService) : ControllerBase
{
    [HttpPost("import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0 || file.Length > 10_000_000)
            return BadRequest("A non-empty HTML catalog under 10 MB is required.");

        await using var stream = file.OpenReadStream();
        var result = await carService.ImportCatalogAsync(stream, cancellationToken);
        return Ok(result);
    }
}
