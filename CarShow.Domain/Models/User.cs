namespace CarShow.Domain.Models
{
    public class User : BaseEntity<Guid>
    {
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string? Email { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        #region Relations
        public ICollection<UserRole> UserRoles { get; set; }
        #endregion
    }
}
