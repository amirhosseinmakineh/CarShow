using CarShow.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/catalog/model-images")]
public sealed class AdminModelImagesController(CarModelImageService images) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await images.GetModelsAsync(cancellationToken));

    [HttpPut("{modelId:long}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(9 * 1024 * 1024)]
    public async Task<IActionResult> Upload(long modelId, IFormFile image, CancellationToken cancellationToken)
    {
        if (image.Length < 12 || image.Length > 8 * 1024 * 1024)
            return BadRequest("Choose a JPEG, PNG, or WebP image under 8 MB.");

        var header = new byte[12];
        await using (var input = image.OpenReadStream())
            await input.ReadExactlyAsync(header, cancellationToken);
        var extension = header switch
        {
            [0xff, 0xd8, 0xff, ..] => ".jpg",
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, ..] => ".png",
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50] => ".webp",
            _ => null
        };
        if (extension is null) return BadRequest("Unsupported image format.");
        await using var stream = image.OpenReadStream();
        var (path, count) = await images.SaveAsync(modelId, stream, extension, cancellationToken);
        return path is null ? NotFound() : Ok(new { imageName = path, updatedCars = count });
    }
}
