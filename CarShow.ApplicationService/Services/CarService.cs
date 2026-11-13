using CarShow.ApplicationService.Import;
using CarShow.ApplicationService.Contract.Dtos.CarDto;
using CarShow.ApplicationService.Contract.Dtos.CategoryDto;
using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.Dtos.ModelDto;
using CarShow.ApplicationService.Contract.Dtos.TipDto;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
namespace CarShow.ApplicationService.Services
{
    public class CarService : ICarSErvicce
    {
        private readonly IBaseRepository<long, Car> carRepository;
        private readonly IUnitOfWork unitOfWork;
        private readonly IBaseRepository<long, Tip> tipRepository;
        private readonly IBaseRepository<long, Model> carModelRepository;
        private readonly IBaseRepository<long, Category> categoryRepository;
        private readonly IBaseRepository<long, Company> companyRepository;

        public CarService(
            IBaseRepository<long, Car> carRepository,
            IBaseRepository<long, Tip> tipRepository,
            IBaseRepository<long, Model> carModelRepository, 
            IBaseRepository<long, Category> categoryRepository, 
            IBaseRepository<long, Company> companyRepository,
            IUnitOfWork unitOfWork)
        {
            this.carRepository = carRepository;
            this.tipRepository = tipRepository;
            this.carModelRepository = carModelRepository;
            this.categoryRepository = categoryRepository;
            this.companyRepository = companyRepository;
            this.unitOfWork = unitOfWork;
        }

        public async Task<Result<object>> CreateCar(CreateCarDto dto)
        {
            var existingCar = await carRepository.GetAll()
                .FirstOrDefaultAsync(x => x.Name == dto.Name);

            if (existingCar != null)
                return Result<object>.Failure("ماشین مورد نظر جهت ثبت در سیستم موجود میباشد");

            var imagePath = await SaveImageAsync(dto.ImageName);

            var car = new Car
            {
                Name = dto.Name,
                StartDate = dto.StartDate,
                Description = dto.Description,
                ImageName = imagePath,
                Price = dto.Price,
                IsDelete = false,
                CarModelId = dto.CarModelId,
                CategoryId = dto.CategoryId,
                CompanyId = dto.CompanyId,
                TipId = dto.TipId,
            };

            await carRepository.Create(car);
            await carRepository.SaveChanges();

            return Result<object>.Success(car, "ماشین مورد نظر با موفقیت ایجاد شد");
        }

        private async Task<string> SaveImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return null;

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/cars");

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);

            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return "/images/cars/" + uniqueFileName; // مسیر ذخیره در دیتابیس
        }


        public async Task<Result<string>> DeleteCar(long id)
        {
            var car = await carRepository.GetById(id);
            if (car != null)
            {
                await carRepository.Delete(car);
                await carRepository.SaveChanges();
                return Result<string>.Success("اتوموبیل با موفقیت حذف شد");
            }
            else
                return Result<string>.Failure("اتوموبیلی یافت نشد");
        }

        public async Task<List<CarDto>> GetAllCars(
            float? minPrice,
            float? maxPrice,
            string? company,
            int? pageSize = 10,
            int? pageNumber = 0)
        {
            var query = carRepository.GetAll();

            if (minPrice.HasValue)
                query = query.Where(c => c.MarketPrice >= (decimal)minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(c => c.MarketPrice <= (decimal)maxPrice.Value);

            if (!string.IsNullOrEmpty(company))
                query = query.Where(c => c.Company.Name == company);

            query = query.OrderBy(c => c.Id);

            var cars = await query
                .Include(c => c.CarModel)
                .Include(c => c.Company)
                .Include(c => c.Tip)
                .Include(c => c.Category)
                .Select(c => new CarDto
                {
                    Id = c.Id,
                    CategoryName = c.Category.Name,
                    Name = c.Name,
                    StartDate = c.YearCalendar ?? string.Empty,
                    Description = c.Description,
                    ImageName = c.ImageName,
                    Price = (float)(c.MarketPrice ?? 0m),
                    carModeName = c.CarModel.Name,
                    CompanyName = c.Company.Name,
                    tipName = c.Tip.Name
                })
                .ToListAsync();

            return cars;
        }


        public async Task<GetCarForCreateDto> GetCarInfoForCreate()
        {
            var categories = categoryRepository.GetAll()
                .Select(x => new CategoryDto { Name = x.Name })
                .ToListAsync();

            var companies = companyRepository.GetAll()
                .Select(x => new CompanyDto { Name = x.Name })
                .ToListAsync();

            var models = carModelRepository.GetAll()
                .Select(x => new ModelDto { Name = x.Name })
                .ToListAsync();

            var tips = tipRepository.GetAll()
                .Select(x => new TipDto { Name = x.Name })
                .ToListAsync();

            await Task.WhenAll(categories, companies, models, tips);

            var info = new GetCarForCreateDto
            {
                Categories = await categories,
                Companys = await companies,
                Models = await models,
                Tips = await tips
            };

            return info;
        }

        public async Task<GetCarForUpdateDto> GetCarInfoForUpdate(long carId)
        {
            var car =await carRepository.GetById(carId);
            if (car != null)
            {
                var categories = categoryRepository.GetAll()
               .Select(x => new CategoryDto { Name = x.Name })
               .ToListAsync();

                var companies = companyRepository.GetAll()
                    .Select(x => new CompanyDto { Name = x.Name })
                    .ToListAsync();

                var models = carModelRepository.GetAll()
                    .Select(x => new ModelDto { Name = x.Name })
                    .ToListAsync();

                var tips = tipRepository.GetAll()
                    .Select(x => new TipDto { Name = x.Name })
                    .ToListAsync();
                await Task.WhenAll(categories, companies, models, tips);
                var info = new GetCarForUpdateDto()
                {
                    Categories = await categories,
                    Companys = await companies,
                    Models = await models,
                    Tips = await tips,
                    dto = new UpdateCarDto()
                    {
                        Create = car.StartDate,
                        Description = car.Description,
                        ImageName = car.ImageName,
                        IsDelete = car.IsDelete,
                        Name = car.Name,
                        Price = car.Price,
                    }

                };
                return info;

            }
            else
                throw new Exception("اتوموبیلی یافت نشد");
                
        }

        public async Task<Result<object>> UpdateCar(UpdateCarDto dto)
        {
            var car = await carRepository.GetAll()
                .FirstOrDefaultAsync(x => x.Id == dto.Id);

            if (car == null)
                return Result<object>.Failure("اتوموبیلی یافت نشد");

            car.Name = dto.Name;
            car.Price = dto.Price;
            car.Description = dto.Description;
            car.ImageName = dto.ImageName;
            car.StartDate = dto.Create;
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
                .Include(x => x.Category)
                .Include(x => x.CarModel)
                .Select(x => new CarDto()
                {
                    carModeName = x.CarModel.Name,
                    CategoryName = x.Category.Name,
                    CompanyName = x.Company.Name,
                    Description = x.Description,
                    ImageName = x.ImageName,
                    Name = x.Name,
                    Price = (float)(x.MarketPrice ?? 0m),
                    tipName = x.Tip.Name,
                    StartDate = x.YearCalendar ?? string.Empty,
                }).FirstOrDefaultAsync();
            return Result<CarDto>.Success(result);
        }
        public async Task<CatalogImportResultDto> ImportCatalogAsync(
            Stream catalogStream, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(catalogStream, System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            var entries = CatalogHtmlParser.Parse(await reader.ReadToEndAsync(cancellationToken));

            // Store all catalog entries; unverified source associations are not independently validated.
            var accepted = entries.Where(x =>
                !string.IsNullOrWhiteSpace(x.Company) &&
                !string.IsNullOrWhiteSpace(x.Model) &&
                !string.IsNullOrWhiteSpace(x.Tip)).ToList();

            static string Key(string value) => string.Join(" ",
                value.Replace('ي', 'ی').Replace('ك', 'ک')
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();

            var companies = (await companyRepository.GetAll().ToListAsync(cancellationToken))
                .GroupBy(x => Key(x.Name)).ToDictionary(x => x.Key, x => x.First());
            var models = (await carModelRepository.GetAll().ToListAsync(cancellationToken))
                .GroupBy(x => Key(x.Name)).ToDictionary(x => x.Key, x => x.First());
            var tips = (await tipRepository.GetAll().ToListAsync(cancellationToken))
                .GroupBy(x => Key(x.Name)).ToDictionary(x => x.Key, x => x.First());

            var newCompanies = 0;
            var newModels = 0;
            var newTips = 0;
            var imported = 0;
            var existing = 0;

            await unitOfWork.BeginTransactionAsync();
            try
            {
                foreach (var entry in accepted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!companies.ContainsKey(Key(entry.Company)))
                    {
                        var company = new Company { Name = entry.Company };
                        companies.Add(Key(entry.Company), company);
                        await companyRepository.Create(company);
                        newCompanies++;
                    }
                    if (!models.ContainsKey(Key(entry.Model)))
                    {
                        var model = new Model { Name = entry.Model };
                        models.Add(Key(entry.Model), model);
                        await carModelRepository.Create(model);
                        newModels++;
                    }
                    if (!tips.ContainsKey(Key(entry.Tip)))
                    {
                        var tip = new Tip { Name = entry.Tip };
                        tips.Add(Key(entry.Tip), tip);
                        await tipRepository.Create(tip);
                        newTips++;
                    }
                }

                var category = await categoryRepository.GetAll()
                    .FirstOrDefaultAsync(x => x.Name == "کاتالوگ", cancellationToken);
                if (category is null)
                {
                    category = new Category { Name = "کاتالوگ" };
                    await categoryRepository.Create(category);
                }

                await unitOfWork.SaveChangesAsync();

                var known = (await carRepository.GetAll()
                    .Where(x => x.CompanyId > 0 && x.CarModelId > 0 && x.TipId > 0)
                    .Select(x => new { x.CompanyId, x.CarModelId, x.TipId })
                    .ToListAsync(cancellationToken))
                    .Select(x => (x.CompanyId, x.CarModelId, x.TipId))
                    .ToHashSet();

                foreach (var entry in accepted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var companyId = companies[Key(entry.Company)].Id;
                    var modelId = models[Key(entry.Model)].Id;
                    var tipId = tips[Key(entry.Tip)].Id;
                    if (!known.Add((companyId, modelId, tipId)))
                    {
                        existing++;
                        continue;
                    }

                    await carRepository.Create(new Car
                    {
                        Name = $"{entry.Brand} {entry.Model} {entry.Tip}".Trim(),
                        CompanyId = companyId,
                        CarModelId = modelId,
                        TipId = tipId,
                        CategoryId = category.Id,
                        YearCalendar = null,
                        MarketPrice = null,
                        FactoryPrice = null,
                        LastUpdated = DateTime.UtcNow,
                        SourceUrl = entry.SourceUrl,
                        Slug = $"catalog-{companyId}-{modelId}-{tipId}",
                        Description = string.Empty,
                        ImageName = string.Empty
                    });
                    imported++;
                }

                await unitOfWork.CommitAsync();
                return new CatalogImportResultDto(entries.Count, imported, existing,
                    entries.Count - accepted.Count, newModels, newCompanies, newTips);
            }
            catch (DbUpdateException ex)
            {
                await unitOfWork.RollbackAsync();
                var entities = string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name));
                var root = ex.GetBaseException();
                throw new InvalidOperationException(
                    $"Catalog seed database failure. Entities: [{entities}]. " +
                    $"SQL error: {root.Message}", ex);
            }
            catch
            {
                await unitOfWork.RollbackAsync();
                throw;
            }
        }

    }
}