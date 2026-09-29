using CarShow.ApplicationService.Contract.Dtos.CategoryDto;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface ICategoryService
    {
        Result<List<CategoryDto>> GetCategories();
        Task<Result<object>> CreateCategory(CreateCategoryDto dto);
        Task<Result<object>> UpdateCategory(UpdateCategoryDto dto);
        Task<Result<string>> DeleteCategory(long id);
    }


}
