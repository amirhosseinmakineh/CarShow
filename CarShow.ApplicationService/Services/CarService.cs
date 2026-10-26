using System.Text.Json;
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
        private readonly ITelegramProcessingStore processingStore;

        public CarService(
            IBaseRepository<long, Car> carRepository,
            IBaseRepository<long, Tip> tipRepository,
            IBaseRepository<long, CarModel> carModelRepository,
            IBaseRepository<long, Category> categoryRepository,
            IBaseRepository<long, Company> companyRepository,
            ITelegramProcessingStore processingStore)
        {
            this.carRepository = carRepository;
            this.tipRepository = tipRepository;
            this.carModelRepository = carModelRepository;
            this.categoryRepository = categoryRepository;
            this.companyRepository = companyRepository;
            this.processingStore = processingStore;
        }

        public async Task<Result<object>> CreateCar(CreateCarDto dto)
        {
            var existingCar = await carRepository.GetAll()
                .FirstOrDefaultAsync(x => x.Name == dto.Name && x.IsUsed == dto.IsUsed);

            if (existingCar != null)
                return Result<object>.Failure("ماشین مورد نظر جهت ثبت در سیستم موجود می‌باشد");

            var imagePath = await SaveImageAsync(dto.ImageName);
            var car = new Car
            {
                Name = dto.Name,
                IsUsed = dto.IsUsed,
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

        public async Task<List<CarDto>> GetAllCars(float? minPrice, float? maxPrice, string? company, bool isUsed = false, int? pageSize = 10, int? pageNumber = 0, string? model = null, string? trim = null, int? year = null, int? minMileage = null, int? maxMileage = null, string? sort = null)
        {
            var page = Math.Max(0, pageNumber.GetValueOrDefault());
            var size = Math.Clamp(pageSize.GetValueOrDefault(10), 1, 100);

            var query = carRepository.GetAll().Where(car => car.IsUsed == isUsed);
            var listings = processingStore.Listings.Where(x => !x.IsDelete && x.CarId != null && x.IsUsed == isUsed);
            if (!string.IsNullOrWhiteSpace(model)) query = query.Where(c => c.Name.Contains(model) || c.CarModel.Name.Contains(model));
            if (!string.IsNullOrWhiteSpace(trim)) query = query.Where(c => c.Tip != null && c.Tip.Name.Contains(trim));
            if (year.HasValue) query = query.Where(c => listings.Any(l => l.CarId == c.Id && l.Year == year));
            if (minMileage.HasValue) query = query.Where(c => listings.Any(l => l.CarId == c.Id && l.Mileage >= minMileage));
            if (maxMileage.HasValue) query = query.Where(c => listings.Any(l => l.CarId == c.Id && l.Mileage <= maxMileage));

            if (minPrice.HasValue)
                query = query.Where(c => c.MarketPrice >= (decimal)minPrice.Value);
            if (maxPrice.HasValue)
                query = query.Where(c => c.MarketPrice <= (decimal)maxPrice.Value);

            if (!string.IsNullOrWhiteSpace(company))
            {
                var companyName = company.Trim();
                var companyIds = await companyRepository.GetAll()
                    .Where(x => x.Name.Trim() == companyName)
                    .Select(x => x.Id)
                    .ToListAsync();

                if (companyIds.Count == 0)
                    return new List<CarDto>();

                query = query.Where(c => companyIds.Contains(c.CompanyId));
            }

            query = sort switch
            {
                "priceAsc" => query.OrderBy(c => c.MarketPrice),
                "priceDesc" => query.OrderByDescending(c => c.MarketPrice),
                _ => query.OrderByDescending(c => c.LastUpdated)
            };
            var rows = await query
                .Skip(page * size)
                .Take(size)
                .Select(c => new
                {
                    c.Id,
                    c.IsUsed,
                    c.Name,
                    c.Description,
                    c.ImageName,
                    c.MarketPrice,
                    c.MarketPriceDay,
                    c.PriceSampleCount,
                    c.PriceCalculatedAt,
                    c.FactoryPrice,
                    c.SourceUrl,
                    c.LastUpdated,
                    c.CompanyId,
                    c.CarModelId,
                    c.TipId,
                    Year = listings.Where(l => l.CarId == c.Id).OrderByDescending(l => l.PublishedAt).Select(l => l.Year).FirstOrDefault(),
                    Mileage = listings.Where(l => l.CarId == c.Id).OrderByDescending(l => l.PublishedAt).Select(l => l.Mileage).FirstOrDefault(),
                    ComparisonAveragePrice = listings.Where(l => l.CarId == c.Id).OrderByDescending(l => l.PublishedAt).Select(l => l.ComparisonAveragePrice).FirstOrDefault(),
                    ComparisonStatus = listings.Where(l => l.CarId == c.Id).OrderByDescending(l => l.PublishedAt).Select(l => l.ComparisonStatus).FirstOrDefault()
                })
                .ToListAsync();

            if (rows.Count == 0)
                return new List<CarDto>();

            var companyIdsForRows = rows.Select(x => x.CompanyId).Distinct().ToList();
            var modelIdsForRows = rows.Select(x => x.CarModelId).Distinct().ToList();
            var tipIdsForRows = rows.Where(x => x.TipId.HasValue).Select(x => x.TipId!.Value).Distinct().ToList();

            var companyNames = await companyRepository.GetAll()
                .Where(x => companyIdsForRows.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);
            var modelNames = await carModelRepository.GetAll()
                .Where(x => modelIdsForRows.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);
            var tipNames = await tipRepository.GetAll()
                .Where(x => tipIdsForRows.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            return rows.Select(c => new CarDto
            {
                Id = c.Id,
                IsUsed = c.IsUsed,
                Year = c.Year,
                Mileage = c.Mileage,
                ComparisonAveragePrice = c.ComparisonAveragePrice,
                ComparisonStatus = c.ComparisonStatus,
                CategoryName = string.Empty,
                Name = c.Name,
                Description = c.Description,
                ImageName = c.ImageName,
                Price = (float)c.MarketPrice,
                MarketPrice = c.MarketPrice,
                MarketPriceDay = c.MarketPriceDay,
                PriceSampleCount = c.PriceSampleCount,
                PriceCalculatedAt = c.PriceCalculatedAt.HasValue
                    ? DateTime.SpecifyKind(c.PriceCalculatedAt.Value, DateTimeKind.Utc) : null,
                FactoryPrice = c.FactoryPrice,
                SourceUrl = c.SourceUrl,
                LastUpdated = c.LastUpdated,
                carModeName = modelNames.GetValueOrDefault(c.CarModelId, string.Empty),
                CompanyName = companyNames.GetValueOrDefault(c.CompanyId, string.Empty),
                tipName = c.TipId.HasValue ? tipNames.GetValueOrDefault(c.TipId.Value, string.Empty) : string.Empty
            }).ToList();
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
            car.IsUsed = dto.IsUsed;
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
            var car = await carRepository.GetAll()
                .Include(x => x.Tip)
                .Include(x => x.Company)
                .Include(x => x.CarModel)
                .FirstOrDefaultAsync(x => x.Id == carId);

            if (car is null)
                return Result<CarDto>.Failure("اتومبیلی یافت نشد");

            CarDetailsDto? details = null;
            if (!string.IsNullOrWhiteSpace(car.Description))
            {
                try
                {
                    details = JsonSerializer.Deserialize<CarDetailsDto>(car.Description);
                }
                catch (JsonException)
                {
                    // Older manually entered descriptions are plain text.
                }
            }

            var result = new CarDto
            {
                Id = car.Id,
                IsUsed = car.IsUsed,
                carModeName = car.CarModel?.Name ?? string.Empty,
                CategoryName = string.Empty,
                CompanyName = car.Company?.Name ?? string.Empty,
                Description = details?.Description ?? car.Description,
                Details = details,
                ImageName = car.ImageName,
                Name = car.Name,
                Price = (float)car.MarketPrice,
                MarketPrice = car.MarketPrice,
                MarketPriceDay = car.MarketPriceDay,
                PriceSampleCount = car.PriceSampleCount,
                PriceCalculatedAt = car.PriceCalculatedAt.HasValue
                    ? DateTime.SpecifyKind(car.PriceCalculatedAt.Value, DateTimeKind.Utc) : null,
                FactoryPrice = car.FactoryPrice,
                SourceUrl = car.SourceUrl,
                LastUpdated = car.LastUpdated,
                tipName = car.Tip?.Name ?? string.Empty
            };

            return Result<CarDto>.Success(result);
        }
    }
}
