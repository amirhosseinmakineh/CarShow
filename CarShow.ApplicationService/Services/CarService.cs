using System.Text.Json;
using System.Data;
using Microsoft.Data.SqlClient;
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
        private readonly string connectionString;
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
            IBaseRepository<long, Company> companyRepository,
            IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("CarShow") ?? throw new InvalidOperationException("CarShow connection string missing.");
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

        // The public list is derived from the exact approved references of the latest
        // completed Telegram scan, rather than from stale Cars.MarketPrice flags.
        public async Task<List<CarDto>> GetAllCars(float? minPrice, float? maxPrice, string? company, int? pageSize = 10, int? pageNumber = 0)
        {
            var page = Math.Max(0, pageNumber.GetValueOrDefault());
            var size = Math.Clamp(pageSize.GetValueOrDefault(10), 1, 100);

            // A scan currently in progress must not expose partial results.
            // The last successfully completed scan remains visible until it is replaced.
            string? resultsJson;
            await using var connection = new SqlConnection(connectionString);
            var closeAfter = connection.State != ConnectionState.Open;
            if (closeAfter) await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    IF OBJECT_ID(N'dbo.TelegramScanJobs', N'U') IS NULL
                        SELECT CAST(NULL AS nvarchar(max));
                    ELSE
                        SELECT TOP (1) ResultsJson FROM dbo.TelegramScanJobs
                        WHERE Status IN (N'Completed', N'PartialSuccess')
                          AND ResultsJson IS NOT NULL
                        ORDER BY Id DESC;
                    """;
                resultsJson = (await command.ExecuteScalarAsync()) as string;
            }
            finally
            {
                if (closeAfter) await connection.CloseAsync();
            }
            if (string.IsNullOrWhiteSpace(resultsJson)) return new List<CarDto>();

            using var document = JsonDocument.Parse(resultsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("References", out var references) ||
                references.ValueKind != JsonValueKind.Array)
                return new List<CarDto>();

            var approved = new List<(long CarId, string Year, decimal Price)>();
            foreach (var reference in references.EnumerateArray())
            {
                if (!reference.TryGetProperty("Name", out var nameNode) ||
                    nameNode.ValueKind != JsonValueKind.String) continue;
                var name = nameNode.GetString();
                if (name is null || !name.StartsWith("catalog:", StringComparison.Ordinal) ||
                    !long.TryParse(name.Substring("catalog:".Length), out var carId)) continue;
                if (!reference.TryGetProperty("Year", out var yearNode) ||
                    !reference.TryGetProperty("ReferencePriceBillion", out var priceNode) ||
                    priceNode.ValueKind != JsonValueKind.Number) continue;
                var year = yearNode.ToString();
                if (string.IsNullOrWhiteSpace(year)) continue;
                var price = priceNode.GetDecimal() * 1_000_000_000m;
                if (price <= 0 || (minPrice.HasValue && price < (decimal)minPrice.Value) ||
                    (maxPrice.HasValue && price > (decimal)maxPrice.Value)) continue;
                approved.Add((carId, year, price));
            }
            if (approved.Count == 0) return new List<CarDto>();

            var ids = approved.Select(x => x.CarId).Distinct().ToArray();
            var cars = await carRepository.GetAll()
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id) && c.CompanyId != null &&
                    c.CarModelId != null && c.TipId != null)
                .Select(c => new
                {
                    c.Id, c.Name, c.Description, c.ImageName, c.FactoryPrice,
                    c.SourceUrl, c.LastUpdated, c.CompanyId, c.CarModelId, c.TipId
                })
                .ToListAsync();

            var companyIds = cars.Select(c => c.CompanyId!.Value).Distinct().ToArray();
            var modelIds = cars.Select(c => c.CarModelId!.Value).Distinct().ToArray();
            var tipIds = cars.Select(c => c.TipId!.Value).Distinct().ToArray();
            var companies = await companyRepository.GetAll().Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            var models = await carModelRepository.GetAll().Where(c => modelIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            var tips = await tipRepository.GetAll().Where(c => tipIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            var carsById = cars.ToDictionary(c => c.Id);

            // One row per reference (car + manufacturing year). Pagination happens
            // after expansion so all years are available independently.
            var rows = approved
                .Where(r => carsById.ContainsKey(r.CarId))
                .Where(r => string.IsNullOrWhiteSpace(company) ||
                    string.Equals(companies.GetValueOrDefault(carsById[r.CarId].CompanyId!.Value)?.Trim(),
                        company.Trim(), StringComparison.Ordinal))
                .OrderBy(r => r.CarId)
                .ThenByDescending(r => r.Year, StringComparer.Ordinal)
                .Skip(page * size).Take(size)
                .Select(r =>
                {
                    var c = carsById[r.CarId];
                    return new CarDto
                    {
                        Id = c.Id,
                        Name = c.Name,
                        carModeName = models.GetValueOrDefault(c.CarModelId!.Value, string.Empty),
                        tipName = tips.GetValueOrDefault(c.TipId!.Value, string.Empty),
                        CompanyName = companies.GetValueOrDefault(c.CompanyId!.Value, string.Empty),
                        StartDate = r.Year,
                        Price = (float)r.Price,
                        MarketPrice = r.Price,
                        FactoryPrice = c.FactoryPrice,
                        ImageName = c.ImageName,
                        Description = c.Description,
                        SourceUrl = c.SourceUrl,
                        LastUpdated = c.LastUpdated,
                        CategoryName = string.Empty
                    };
                }).ToList();
            return rows;
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
                CategoryName = string.Empty,
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
