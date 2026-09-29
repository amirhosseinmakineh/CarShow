using CarShow.ApplicationService.Contract.Dtos.CategoryDto;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly ICategoryService categoryService;

        public CategoryController(ICategoryService categoryService)
        {
            this.categoryService = categoryService;
        }
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var result =  categoryService.GetCategories();
            return Ok(result);
        }
        [HttpPost("CreateCategory")]
        public async Task<IActionResult> CreateCategory(CreateCategoryDto dto)
        {
            var result = await categoryService.CreateCategory(dto);
            return Ok(result);
        }
        [HttpPost("UpdateCategory")]
        public async Task<IActionResult> UpdateCategory(UpdateCategoryDto dto)
        {
            var result = await categoryService.UpdateCategory(dto);
            return Ok(result);

        }
        [HttpGet("DeleteCategory/{id}")]
        public async Task<IActionResult> DeleteCategory(long id)
        {
            var result = await categoryService.DeleteCategory(id);
            return Ok(result);
        }
    }
}
