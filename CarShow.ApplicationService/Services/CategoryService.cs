using CarShow.ApplicationService.Contract.Dtos.CategoryDto;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;

namespace CarShow.ApplicationService.Services
{
    public class CategoryService : ICategoryService 
    {
        private readonly IBaseRepository<long, Category> categoryRepository;

        public CategoryService(IBaseRepository<long, Category> categoryRepository)
        {
            this.categoryRepository = categoryRepository;
        }

        public async Task<Result<object>> CreateCategory(CreateCategoryDto dto)
        {
            var category = categoryRepository.GetAll()
                .FirstOrDefault(x=> x.Name == dto.Name);
            if (category is  null)
            {
                category = new Category()
                {
                    Name = dto.Name,
                };
                await categoryRepository.Create(category);
                await categoryRepository.SaveChanges();
                return Result<object>.Success(category, "دسته بندی جدید با موفقیت ثبت شد");
            }
            else
                return Result<object>.Failure("دسته بندی وارد شده در سیستم وجود ندارد");
        }

        public async Task<Result<string>> DeleteCategory(long id)
        {
            var category = await categoryRepository.GetById(id);
            if(category is not null)
            {
                 await categoryRepository.Delete(category);
                await categoryRepository.SaveChanges();
                return Result<string>.Success("حذف با موفقت انجام شد");

            }
            else
                return Result<string>.Failure("دسته بندی ای بافت نشد ");
        }

        public Result<List<CategoryDto>> GetCategories()
        {
            var result =  categoryRepository.GetAll().Select(x=> new CategoryDto()
            {
                Id = x.Id,
                Name = x.Name,
            }).ToList();
            return Result<List<CategoryDto>>.Success(result);
        }

        public async Task<Result<object>> UpdateCategory(UpdateCategoryDto dto)
        {
            var category = await categoryRepository.GetById(dto.Id);
            if (category is not null)
            {
                category.Id = dto.Id;
                category.Name = dto.Name;
                await categoryRepository.Update(category);
                await categoryRepository.SaveChanges();
                return Result<object>.Success(category, "دسته بندی جدید با موفقیت ثبت شد");
            }
            else
                return Result<object>.Failure("دسته بندی وارد شده در سیستم موجود میباشد");
        }
    }
}
