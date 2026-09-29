using CarShow.ApplicationService.Contract.Dtos.OrderItem;

namespace CarShow.ApplicationService.Contract.Dtos.User
{
    public record RegisterDto 
    {
        public Guid Id { get; set; }
        public string MobileNumber { get; set; } = string.Empty;
        public string? Emaail { get; set; }
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
    }
    public record AdddRoleToUserDto
    {

        public Guid UserId { get; set; }
        public long RoleId { get; set; }
    }
    public record CreateUserDto
    {
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string? Email { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RoleName { get; set; }
    }
    public record UpdateUserDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string? Email { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
    public record UserDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string? Email { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
    }

}
