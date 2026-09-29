using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoleController : ControllerBase
    {
        private readonly IUserService userService;

        public RoleController(IUserService userService)
        {
            this.userService = userService;
        }

        [HttpGet]
        public IActionResult GetAllRoles()
        {
            var result = userService.GetAllRoles();
            return Ok(result);
        }
    }
}
