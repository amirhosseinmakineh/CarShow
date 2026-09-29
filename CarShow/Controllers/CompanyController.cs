using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CompanyController : ControllerBase
    {
        private readonly ICompanyService companyService;

        public CompanyController(ICompanyService companyService)
        {
            this.companyService = companyService;
        }
        [HttpGet]
        public IActionResult GetCompanies()
        {
            var result = companyService.GetCompanies();
            return Ok(result);
        }
        [HttpPost("CreateCompany")]
        public async Task<IActionResult> CreateCompany(CreateCompanyDto dto)
        {
            var result = await companyService.CreateCompany(dto);
            return Ok(result);
        }
        [HttpPost("UpdateCompany")]
        public async Task<IActionResult> UpdateCompany(UpdateCompanyDto dto)
        {
            var result = await companyService.UpdateCompany(dto);
            return Ok(result);
        }
        [HttpGet("DeleteCompany/{id}")]
        public async Task<IActionResult> DeletCompany(long id)
        {
            var result = await companyService.DeleteCompany(id);
            return Ok(result);
        }

    }
}
