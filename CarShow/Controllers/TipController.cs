using CarShow.ApplicationService.Contract.Dtos.TipDto;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TipController : ControllerBase
    {
        private readonly ITipService tipService;

        public TipController(ITipService tipService)
        {
            this.tipService = tipService;
        }
        [HttpGet]
        public IActionResult GetTips()
        {
            var result = tipService.GetTips();
            return Ok(result);
        }
        [HttpPost("CreateTip")]
        public async Task<IActionResult> CreateTip(CreateTipDto dto)
        {
            var result = await tipService.CreateTip(dto);
            return Ok(result);
        }
        [HttpPost("UpdateTip")]
        public async Task<IActionResult> UpdateTip(UpdateTipDto dto) 
        {
            var result  = await tipService.UpdateTip(dto);
            return Ok(result);
        }
        [HttpGet("DeleteTip/{id}")]
        public async Task<IActionResult> DeleteTip(long id) 
        {
            var result = await tipService.DeleteTip(id);
            return Ok(result);
        }

    }
}
