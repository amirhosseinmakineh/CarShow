using CarShow.ApplicationService.Contract.Dtos.ModelDto;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ModelController : ControllerBase
    {
        private readonly IModelService modelService;

        public ModelController(IModelService modelService)
        {
            this.modelService = modelService;
        }
        [HttpGet]
        public IActionResult GetModels()
        {
            var result = modelService.GetModels();
            return Ok(result);
        }
        [HttpPost("CreateModel")]
        public async Task<IActionResult> CreateModel(CreateModelDto dto)
        {
            var result = await modelService.CreateModel(dto);
            return Ok(result);
        }
        [HttpPost("UpdateModel")]
        public async Task<IActionResult> UpdateModel(UpdateModelDto dto)
        {
           var result = await modelService.UpdateModel(dto);
            return Ok(result);  
        }
        [HttpGet("DeleteModel/{id}")]
        public async Task<IActionResult> DeleteModel(long id)
        {
            var result = await modelService.DeleteModel(id);
            return Ok(result);
        }

    }
}
