using CarShow.ApplicationService.Contract.Dtos.Order;
using CarShow.ApplicationService.Contract.Dtos.RoleDto;
using CarShow.ApplicationService.Contract.Dtos.User;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface IUserService
    {
        Task<Result<RegisterDto>> Register(RegisterDto dto);
        Task<Result<LoginDto>> Login(LoginDto dto);
        Task AddRoleToUser(Guid userId, string roleName);
        Task<Result<object>> CreateUser(CreateUserDto dto);
        Task<Result<object>> UpdateUser(UpdateUserDto dto);
        Task<Result<string>> DeleteUser(Guid id);
        List<UserDto> GetAllUsers(int pageSize = 10, int pageNumber = 0);
        List<RoleDto> GetAllRoles();
    }
}