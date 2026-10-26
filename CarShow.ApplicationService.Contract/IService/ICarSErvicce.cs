using CarShow.ApplicationService.Contract.Dtos.CarDto;
using CarShow.ApplicationService.Contract.Dtos.TipDto;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface ICarSErvicce
    {
        Task<Result<object>> CreateCar(CreateCarDto dto);
        Task<Result<object>> UpdateCar(UpdateCarDto dto);
        Task<List<CarDto>> GetAllCars(float? minPrice, float? maxPrice, string? company, bool isUsed = false, int? pageSize = 10, int? pageNumber = 0, string? model = null, string? trim = null, int? year = null, int? minMileage = null, int? maxMileage = null, string? sort = null);
        Task<Result<string>> DeleteCar(long id);
        Task<GetCarForCreateDto> GetCarInfoForCreate();
        Task<GetCarForUpdateDto> GetCarInfoForUpdate(long carId);
        Task<Result<CarDto>> GetCarDetail(long carId);
    }


}
