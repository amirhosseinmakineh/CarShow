using CarShow.ApplicationService.Contract.Dtos.User;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService userService;

        public UserController(IUserService userService)
        {
            this.userService = userService;
        }
        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var result = await userService.Login(dto);
            return Ok(result);
        }
        [HttpPost]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            var result = await userService.Register(dto);
            return Ok(result);
        }
        [Authorize(Roles = "Admin")]
        [HttpGet("GetUsers")]
        public IActionResult GetAllUsers(int pageSize = 10, int pageNumber = 0)
        {
            var result = userService.GetAllUsers(pageSize, pageNumber);
            return Ok(result);
        }
        [HttpPost("CreateUser")]
        public async Task<IActionResult> CreateUser(CreateUserDto dto)
        {
            var result = await userService.CreateUser(dto);
            return Ok(result);
        }
        [HttpPost("UpdateUser")]
        public async Task<IActionResult> UpdateUser(UpdateUserDto dto)
        {
            var result = await userService.UpdateUser(dto);
            return Ok(result);
        }
        [HttpGet("DeleteUser/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var result = await userService.DeleteUser(id);
            return Ok(result);
            
        }

    }

}
