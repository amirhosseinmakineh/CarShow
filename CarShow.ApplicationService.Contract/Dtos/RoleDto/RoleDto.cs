namespace CarShow.ApplicationService.Contract.Dtos.RoleDto
{
    public record RoleDto
    {
        public long RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }
}
