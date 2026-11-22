using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Data;
using Microsoft.Data.SqlClient;
using CarShow.ApplicationService.Contract.Dtos.CarDto;
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
        private readonly string connectionString;
        private readonly string imageDirectory;
        private readonly IBaseRepository<long, Car> carRepository;
        private readonly IBaseRepository<long, Tip> tipRepository;
        private readonly IBaseRepository<long, CarModel> carModelRepository;
        private readonly IBaseRepository<long, Company> companyRepository;

        public CarService(
            IBaseRepository<long, Car> carRepository,
            IBaseRepository<long, Tip> tipRepository,
            IBaseRepository<long, CarModel> carModelRepository,
            IBaseRepository<long, Company> companyRepository,
            IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("CarShow") ?? throw new InvalidOperationException("CarShow connection string missing.");
            imageDirectory = GetImageDirectory(configuration);
            this.carRepository = carRepository;
            this.tipRepository = tipRepository;
            this.carModelRepository = carModelRepository;
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

        public async Task<string?> UpdateCarImage(long id, IFormFile image, string extension, CancellationToken cancellationToken)
        {
            var cars = carRepository.GetAll().Where(car => car.Id == id && !car.IsDelete);
            if (!await cars.AnyAsync(cancellationToken))
                return null;

            var imagePath = await SaveImageAsync(image, extension);
            var updated = await cars.ExecuteUpdateAsync(
                setters => setters.SetProperty(car => car.ImageName, imagePath!), cancellationToken);
            return updated == 0 ? null : imagePath;
        }

        public static string GetImageDirectory(IConfiguration? configuration = null)
        {
            var configured = configuration?["VehicleImages:Directory"];
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);

            // Runflare mounts the existing persistent volume here. Local runs use wwwroot.
            const string persistentVolume = "/data/licensing";
            return Directory.Exists(persistentVolume)
                ? Path.Combine(persistentVolume, "vehicle-images")
                : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "cars");
        }

        private async Task<string?> SaveImageAsync(IFormFile? file, string? extension = null)
        {
            if (file is null || file.Length == 0)
                return null;

            var uploadsFolder = imageDirectory;
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{extension ?? Path.GetExtension(file.FileName)}";
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

        // The public list is derived from the exact approved references of the latest
        // completed Telegram scan, rather than from stale Cars.MarketPrice flags.
        private async Task<(List<CarDto> Rows, int ReferenceCount, int MissingCars)> LoadReferenceCars(
            float? minPrice, float? maxPrice, string? company)
        {
            // Cars is the source of truth for public visibility. Historical years
            // in Description are audit-only and must never reactivate stale prices.
            var approvedCars = await carRepository.GetAll()
                .AsNoTracking()
                .Where(c => c.CarModelId != null && c.TipId != null
                    && c.IsReferenceApproved && c.Description != null)
                .Select(c => new
                {
                    c.Id, c.Name, c.Description, c.ImageName, c.FactoryPrice,
                    c.SourceUrl, c.LastUpdated, c.CompanyId, c.CarModelId, c.TipId
                })
                .ToListAsync();

            var companyIds = approvedCars.Where(c => c.CompanyId.HasValue)
                .Select(c => c.CompanyId!.Value).Distinct().ToArray();
            var modelIds = approvedCars.Select(c => c.CarModelId!.Value).Distinct().ToArray();
            var tipIds = approvedCars.Select(c => c.TipId!.Value).Distinct().ToArray();
            var companies = await companyRepository.GetAll()
                .Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
            var models = await carModelRepository.GetAll()
                .Where(c => modelIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
            var tips = await tipRepository.GetAll()
                .Where(c => tipIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);

            var rows = new List<CarDto>();
            foreach (var car in approvedCars)
            {
                if (!string.IsNullOrWhiteSpace(company)
                    && (!car.CompanyId.HasValue
                        || !string.Equals(companies.GetValueOrDefault(car.CompanyId.Value)?.Trim(),
                            company.Trim(), StringComparison.OrdinalIgnoreCase)))
                    continue;

                using var doc = JsonDocument.Parse(car.Description);
                var root = doc.RootElement;
                var references = new List<(string Year, decimal Price)>();
                if (root.TryGetProperty("CurrentYears", out var currentYears)
                    && currentYears.ValueKind == JsonValueKind.Object)
                {
                    foreach (var year in currentYears.EnumerateObject())
                    {
                        if (!year.Value.TryGetProperty("ReferencePriceBillion", out var priceNode)
                            || priceNode.ValueKind != JsonValueKind.Number
                            || !priceNode.TryGetDecimal(out var priceBillion)
                            || priceBillion <= 0) continue;
                        references.Add((year.Name, priceBillion * 1_000_000_000m));
                    }
                }
                else if (root.TryGetProperty("Year", out var yearNode)
                    && root.TryGetProperty("ReferencePriceBillion", out var priceNode)
                    && priceNode.ValueKind == JsonValueKind.Number
                    && priceNode.TryGetDecimal(out var priceBillion) && priceBillion > 0)
                {
                    // Compatibility for cars approved before CurrentYears was added.
                    var year = yearNode.ToString();
                    if (!string.IsNullOrWhiteSpace(year))
                        references.Add((year, priceBillion * 1_000_000_000m));
                }

                foreach (var reference in references)
                {
                    if ((minPrice.HasValue && reference.Price < (decimal)minPrice.Value)
                        || (maxPrice.HasValue && reference.Price > (decimal)maxPrice.Value))
                        continue;
                    rows.Add(new CarDto
                    {
                        Id = car.Id,
                        Name = car.Name,
                        carModeName = models.GetValueOrDefault(car.CarModelId!.Value, string.Empty),
                        tipName = tips.GetValueOrDefault(car.TipId!.Value, string.Empty),
                        CompanyName = car.CompanyId.HasValue
                            ? companies.GetValueOrDefault(car.CompanyId.Value, string.Empty) : string.Empty,
                        StartDate = reference.Year,
                        Price = (float)reference.Price,
                        MarketPrice = reference.Price,
                        FactoryPrice = car.FactoryPrice,
                        ImageName = car.ImageName,
                        Description = car.Description,
                        SourceUrl = car.SourceUrl,
                        LastUpdated = car.LastUpdated,
                    });
                }
            }
            rows = rows.OrderBy(r => r.Id)
                .ThenByDescending(r => r.StartDate, StringComparer.Ordinal).ToList();
            return (rows, rows.Count, 0);
        }

        // Retain the original array response for existing clients.
        public async Task<List<CarDto>> GetAllCars(float? minPrice, float? maxPrice, string? company, int? pageSize = 10, int? pageNumber = 0)
        {
            var (rows, _, _) = await LoadReferenceCars(minPrice, maxPrice, company);
            var page = Math.Max(0, pageNumber.GetValueOrDefault());
            var size = Math.Clamp(pageSize.GetValueOrDefault(10), 1, 100);
            return rows.Skip((long)page * size > int.MaxValue ? int.MaxValue : page * size).Take(size).ToList();
        }

        public async Task<object> GetPagedReferenceCars(float? minPrice, float? maxPrice, string? company, int pageSize = 10, int pageNumber = 0)
        {
            var (rows, referenceCount, missingCars) = await LoadReferenceCars(minPrice, maxPrice, company);
            var page = Math.Max(0, pageNumber);
            var size = Math.Clamp(pageSize, 1, 100);
            var offset = (long)page * size;
            return new
            {
                Items = rows.Skip(offset > int.MaxValue ? int.MaxValue : (int)offset).Take(size).ToList(),
                TotalCount = rows.Count,
                PageNumber = page,
                PageSize = size,
                TotalPages = (rows.Count + size - 1) / size,
                ApprovedReferenceCount = referenceCount,
                MissingCatalogMatches = missingCars
            };
        }

        public async Task<GetCarForCreateDto> GetCarInfoForCreate()
        {
            var companies = companyRepository.GetAll()
                .Select(x => new CompanyDto { Name = x.Name }).ToListAsync();
            var models = carModelRepository.GetAll()
                .Select(x => new ModelDto { Name = x.Name }).ToListAsync();
            var tips = tipRepository.GetAll()
                .Select(x => new TipDto { Name = x.Name }).ToListAsync();

            await Task.WhenAll(companies, models, tips);
            return new GetCarForCreateDto
            {
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

            var companies = companyRepository.GetAll().Select(x => new CompanyDto { Name = x.Name }).ToListAsync();
            var models = carModelRepository.GetAll().Select(x => new ModelDto { Name = x.Name }).ToListAsync();
            var tips = tipRepository.GetAll().Select(x => new TipDto { Name = x.Name }).ToListAsync();

            await Task.WhenAll(companies, models, tips);
            return new GetCarForUpdateDto
            {
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

        private static string ReadReferenceYear(string description)
        {
            try
            {
                using var document = JsonDocument.Parse(description);
                if (document.RootElement.TryGetProperty("Type", out var type) &&
                    type.GetString() == "TelegramPriceReference" &&
                    document.RootElement.TryGetProperty("Year", out var year))
                    return year.GetString() ?? string.Empty;
            }
            catch (JsonException) { }
            return string.Empty;
        }

        public async Task<Result<CarDto>> GetCarDetail(long carId)
        {
            var car = await carRepository.GetAll()
                .Include(x => x.Tip)
                .Include(x => x.Company)
                .Include(x => x.CarModel)
                .FirstOrDefaultAsync(x => x.Id == carId && x.CompanyId != null &&
                    x.CarModelId != null && x.TipId != null && x.MarketPrice > 0 &&
                    x.Description.Contains("TelegramPriceReference") &&
                    x.Description.Contains("\"ComparisonApproved\":true"));

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
                carModeName = car.CarModel?.Name ?? string.Empty,
                CompanyName = car.Company?.Name ?? string.Empty,
                Description = details?.Description ?? car.Description,
                Details = details,
                ImageName = car.ImageName,
                Name = car.Name,
                Price = (float)car.MarketPrice,
                MarketPrice = car.MarketPrice,
                FactoryPrice = car.FactoryPrice,
                SourceUrl = car.SourceUrl,
                LastUpdated = car.LastUpdated,
                tipName = car.Tip?.Name ?? string.Empty,
                StartDate = ReadReferenceYear(car.Description)
            };

            return Result<CarDto>.Success(result);
        }
    }
}
