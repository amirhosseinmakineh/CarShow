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
        object GetAllUsers(int pageSize = 15, int pageNumber = 0, string? search = null);
        List<RoleDto> GetAllRoles();
    }
        public object GetAllUsers(int pageSize = 15, int pageNumber = 0, string? search = null)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            pageNumber = Math.Max(0, pageNumber);
            var query = _userRepository.GetAll().AsNoTracking().Where(x => !x.IsDelete);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x => x.Name.Contains(term) || x.Family.Contains(term)
                    || (x.Email != null && x.Email.Contains(term)) || x.MobileNumber.Contains(term));
            }
            var totalCount = query.Count();
            var users = query.OrderBy(x => x.Id)
                .Skip((int)Math.Min((long)pageNumber * pageSize, int.MaxValue))
                .Take(pageSize)
                .Select(x => new UserDto
                {
                    Id = x.Id, Name = x.Name, Family = x.Family,
                    Email = x.Email, MobileNumber = x.MobileNumber,
                    RoleName = _userRoleRepository.GetAll()
                        .Where(ur => ur.UserId == x.Id)
                        .Join(_roleRepository.GetAll(), ur => ur.RoleId, r => r.Id, (ur, r) => r.RoleName)
                        .FirstOrDefault()
                }).ToList();
            return new { Items = users, TotalCount = totalCount, PageNumber = pageNumber, PageSize = pageSize };
        }

}