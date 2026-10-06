using CarShow.ApplicationService.Contract.Dtos.CarDto;
using CarShow.ApplicationService.Contract.Dtos.CategoryDto;
using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.Dtos.ModelDto;
using CarShow.ApplicationService.Contract.Dtos.TipDto;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services
{
    public class CarService : ICarSErvicce
    {
        private readonly IBaseRepository<long, Car> carRepository;
        private readonly IBaseRepository<long, Tip> tipRepository;
        private readonly IBaseRepository<long, CarModel> carModelRepository;
        private readonly IBaseRepository<long, Category> categoryRepository;
        private readonly IBaseRepository<long, Company> companyRepository;

        public CarService(
            IBaseRepository<long, Car> carRepository,
            IBaseRepository<long, Tip> tipRepository,
            IBaseRepository<long, CarModel> carModelRepository,
            IBaseRepository<long, Category> categoryRepository,
            IBaseRepository<long, Company> companyRepository)
        {
            this.carRepository = carRepository;
            this.tipRepository = tipRepository;
            this.carModelRepository = carModelRepository;
            this.categoryRepository = categoryRepository;
            this.companyRepository = companyRepository;
        }

        public async Task<Result<object>> CreateCar(CreateCarDto dto)
        {
            var existingCar = await carRepository.GetAll()
                .FirstOrDefaultAsync(x => x.Name == dto.Name);

            if (existingCar != null)
                return Result<object>.Failure("ماشین مورد نظر جهت ثبت در سیستم موجود می‌باشد");

            var imagePath = await SaveImageAsync(dto.ImageName);
            var car = new Car
            {
                Name = dto.Name,
                Description = dto.Description,
                ImageName = imagePath ?? string.Empty,
                IsDelete = false,
                CarModelId = dto.CarModelId,
                CompanyId = dto.CompanyId,
                TipId = dto.TipId
            };

            await carRepository.Create(car);
            await carRepository.SaveChanges();
            return Result<object>.Success(car, "ماشین مورد نظر با موفقیت ایجاد شد");
        }

        private static async Task<string?> SaveImageAsync(IFormFile? file)
        {
            if (file is null || file.Length == 0)
                return null;

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/cars");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            return $"/images/cars/{uniqueFileName}";
        }

        public async Task<Result<string>> DeleteCar(long id)
        {
            var car = await carRepository.GetById(id);
            if (car is null)
                return Result<string>.Failure("اتوموبیلی یافت نشد");

            await carRepository.Delete(car);
            await carRepository.SaveChanges();
            return Result<string>.Success("اتوموبیل با موفقیت حذف شد");
        }

        public async Task<List<CarDto>> GetAllCars(float? minPrice, float? maxPrice, string? company, int? pageSize = 10, int? pageNumber = 0)
        {
            var query = carRepository.GetAll();

            if (minPrice.HasValue)
                query = query.Where(c => c.MarketPrice >= (decimal)minPrice.Value);
            if (maxPrice.HasValue)
                query = query.Where(c => c.MarketPrice <= (decimal)maxPrice.Value);
            if (!string.IsNullOrWhiteSpace(company))
                query = query.Where(c => c.Company.Name == company);

            var page = pageNumber.GetValueOrDefault();
            var size = pageSize.GetValueOrDefault(10);
            var cars = await query
                .Include(c => c.CarModel)
                .Include(c => c.Company)
                .Include(c => c.Tip)
                .OrderBy(c => c.Id)
                .Skip(Math.Max(0, page) * size)
                .Take(size)
                .Select(c => new CarDto
                {
                    Id = c.Id,
                    CategoryName = string.Empty,
                    Name = c.Name,
                    Description = c.Description,
                    ImageName = c.ImageName,
                    Price = (float)c.MarketPrice,
                    MarketPrice = c.MarketPrice,
                    FactoryPrice = c.FactoryPrice,
                    SourceUrl = c.SourceUrl,
                    LastUpdated = c.LastUpdated,
                    carModeName = c.CarModel.Name,
                    CompanyName = c.Company.Name,
                    tipName = c.Tip != null ? c.Tip.Name : string.Empty
                })
                .ToListAsync();

            return cars;
        }

        public async Task<GetCarForCreateDto> GetCarInfoForCreate()
        {
            var categories = categoryRepository.GetAll()
                .Select(x => new CategoryDto { Name = x.Name }).ToListAsync();
            var companies = companyRepository.GetAll()
                .Select(x => new CompanyDto { Name = x.Name }).ToListAsync();
            var models = carModelRepository.GetAll()
                .Select(x => new ModelDto { Name = x.Name }).ToListAsync();
            var tips = tipRepository.GetAll()
                .Select(x => new TipDto { Name = x.Name }).ToListAsync();

            await Task.WhenAll(categories, companies, models, tips);
            return new GetCarForCreateDto
            {
                Categories = await categories,
                Companys = await companies,
                Models = await models,
                Tips = await tips
            };
        }

        public async Task<GetCarForUpdateDto> GetCarInfoForUpdate(long carId)
        {
            var car = await carRepository.GetById(carId);
            if (car is null)
                throw new Exception("اتوموبیلی یافت نشد");

            var categories = categoryRepository.GetAll().Select(x => new CategoryDto { Name = x.Name }).ToListAsync();
            var companies = companyRepository.GetAll().Select(x => new CompanyDto { Name = x.Name }).ToListAsync();
            var models = carModelRepository.GetAll().Select(x => new ModelDto { Name = x.Name }).ToListAsync();
            var tips = tipRepository.GetAll().Select(x => new TipDto { Name = x.Name }).ToListAsync();

            await Task.WhenAll(categories, companies, models, tips);
            return new GetCarForUpdateDto
            {
                Categories = await categories,
                Companys = await companies,
                Models = await models,
                Tips = await tips,
                dto = new UpdateCarDto
                {
                    Description = car.Description,
                    ImageName = car.ImageName,
                    IsDelete = car.IsDelete,
                    Name = car.Name,
                    Price = (float)car.MarketPrice
                }
            };
        }

        public async Task<Result<object>> UpdateCar(UpdateCarDto dto)
        {
            var car = await carRepository.GetById(dto.Id);
            if (car is null)
                return Result<object>.Failure("اتوموبیلی یافت نشد");

            car.Name = dto.Name;
            car.MarketPrice = (decimal)dto.Price;
            car.Description = dto.Description;
            car.ImageName = dto.ImageName;
            car.IsDelete = dto.IsDelete;

            await carRepository.Update(car);
            await carRepository.SaveChanges();
            return Result<object>.Success(car, "ویرایش اتوموبیل مورد نظر با موفقیت انجام شد");
        }

        public async Task<Result<CarDto>> GetCarDetail(long carId)
        {
            var result = await carRepository.GetAll()
                .Include(x => x.Tip)
                .Include(x => x.Company)
                .Include(x => x.CarModel)
                .Where(x => x.Id == carId)
                .Select(x => new CarDto
                {
                    carModeName = x.CarModel.Name,
                    CategoryName = string.Empty,
                    CompanyName = x.Company.Name,
                    Description = x.Description,
                    ImageName = x.ImageName,
                    Name = x.Name,
                    Price = (float)x.MarketPrice,
                    tipName = x.Tip != null ? x.Tip.Name : string.Empty
                })
                .FirstOrDefaultAsync();

            return result is null
                ? Result<CarDto>.Failure("اتوموبیلی یافت نشد")
                : Result<CarDto>.Success(result);
        }
    }
}
