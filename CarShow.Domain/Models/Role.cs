namespace CarShow.Domain.Models
{
    public class Role : BaseEntity<long>
    {
        public string RoleName { get; set; }
        #region Relations
        public ICollection<UserRole> UserRoles { get; set; }
        #endregion
    }
}
