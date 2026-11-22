using CarShow.ApplicationService.Contract.Dtos.CarDto;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CarController : ControllerBase
    {
        private readonly ICarSErvicce servicce;

        public CarController(ICarSErvicce servicce)
        {
            this.servicce = servicce;
        }
        [HttpGet]
        public async Task<IActionResult> GetAllCars(float? minPrice,float? maxPrice,string? company,int pageSize = 10,int pageNumber = 0)
        {
            var result = await servicce.GetAllCars(minPrice,maxPrice,company,pageSize,pageNumber);
            return Ok(result);
        }

        [HttpGet("references")]
        public async Task<IActionResult> GetReferenceCars(
            [FromQuery] float? minPrice, [FromQuery] float? maxPrice,
            [FromQuery] string? company, [FromQuery] int pageSize = 10,
            [FromQuery] int pageNumber = 0)
        {
            var result = await servicce.GetPagedReferenceCars(minPrice, maxPrice, company, pageSize, pageNumber);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> CreateCar(CreateCarDto dto)
        {
            var result = await servicce.CreateCar(dto);
            return Ok(result);
        }
        [Authorize(Roles = "Admin")]
        [HttpPatch]
        public async Task<IActionResult> UpdateCar(UpdateCarDto dto)
        {
            var result = await servicce.UpdateCar(dto);
            return Ok(result);
        }
        [Authorize(Roles = "Admin")]
        [HttpPut("{id:long}/image")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(9 * 1024 * 1024)]
        public async Task<IActionResult> UpdateCarImage(long id, IFormFile image, CancellationToken cancellationToken)
        {
            if (image.Length < 12 || image.Length > 8 * 1024 * 1024)
                return BadRequest("Choose a JPEG, PNG, or WebP image under 8 MB.");

            var header = new byte[12];
            await using (var stream = image.OpenReadStream())
                await stream.ReadExactlyAsync(header, cancellationToken);

            var extension = header switch
            {
                [0xff, 0xd8, 0xff, ..] => ".jpg",
                [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, ..] => ".png",
                [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50] => ".webp",
                _ => null
            };
            if (extension is null)
                return BadRequest("Unsupported image format.");

            var path = await servicce.UpdateCarImage(id, image, extension, cancellationToken);
            return path is null ? NotFound() : Ok(new { imageName = path });
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCar(long id)
        {
           var result = await servicce.DeleteCar(id);
            return Ok(result);
        }
        [HttpGet("GetCarDetail/{carId}")] // حذف /{carId} از اینجا
        public async Task<IActionResult> GetCarDetail(long carId) // اضافه کردن FromQuery
        {
            var result = await servicce.GetCarDetail(carId);
            return Ok(result);
        }



    }
}
