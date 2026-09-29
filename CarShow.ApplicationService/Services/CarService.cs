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
        private readonly IBaseRepository<long, Tip> tipRepository;
        private readonly IBaseRepository<long, Model> carModelRepository;
        private readonly IBaseRepository<long, Category> categoryRepository;
        private readonly IBaseRepository<long, Company> companyRepository;

        public CarService(
            IBaseRepository<long, Car> carRepository,
            IBaseRepository<long, Tip> tipRepository,
            IBaseRepository<long, Model> carModelRepository, 
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
                query = query.Where(c => c.Price >= minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(c => c.Price <= maxPrice.Value);

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
                    StartDate = c.StartDate,
                    Description = c.Description,
                    ImageName = c.ImageName,
                    Price = c.Price,
                    carModeName = c.CarModel.Name,
                    CompanyName = c.Company.Name,
                    tipName = c.Tip.TipName
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
                .Select(x => new TipDto { Name = x.TipName })
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
                    .Select(x => new TipDto { Name = x.TipName })
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
                    Price = x.Price,
                    tipName = x.Tip.TipName,
                    StartDate = x.StartDate,
                }).FirstOrDefaultAsync();
            return Result<CarDto>.Success(result);
        }
    }
}