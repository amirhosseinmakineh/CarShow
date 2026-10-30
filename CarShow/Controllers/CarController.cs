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
        public async Task<IActionResult> GetAllCars(float? minPrice, float? maxPrice, string? company, bool? isUsed = null, int pageSize = 10, int pageNumber = 0, string? model = null, string? trim = null, int? year = null, int? minMileage = null, int? maxMileage = null, string? sort = null)
        {
            var result = await servicce.GetAllCars(minPrice, maxPrice, company, isUsed, pageSize, pageNumber, model, trim, year, minMileage, maxMileage, sort);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCar(CreateCarDto dto)
        {
            await servicce.CreateCar(dto);
            return Ok(dto);
        }
        [HttpPatch]
        public async Task<IActionResult> UpdateCar(UpdateCarDto dto)
        {
            await servicce.UpdateCar(dto);
            return Ok(dto);
        }
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteCar(long id)
        {
           var result = await servicce.DeleteCar(id);
            return result.IsSuccess ? NoContent() : NotFound(result);
        }
        [HttpGet("GetCarDetail/{carId}")] // حذف /{carId} از اینجا
        public async Task<IActionResult> GetCarDetail(long carId) // اضافه کردن FromQuery
        {
            var result = await servicce.GetCarDetail(carId);
            return Ok(result);
        }



    }
}
