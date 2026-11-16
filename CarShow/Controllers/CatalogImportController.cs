using CarShow.ApplicationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Route("api/admin/catalog")]
[Authorize(Roles = "Admin")]
public sealed class CatalogImportController(CatalogImportService importer) : ControllerBase
{
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0 || file.Length > 10_000_000)
            return BadRequest("A non-empty HTML catalog under 10 MB is required.");
        await using var stream = file.OpenReadStream();
        return Ok(await importer.ImportAsync(stream, cancellationToken));
    }
}
