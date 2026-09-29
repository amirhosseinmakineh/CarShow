namespace CarShow.Domain.Models
{
    public class UserRole : BaseEntity<long>
    {
        public Guid UserId { get; set; }
        public long RoleId { get; set; }
        #region Relations
        public User User { get; set; }
        public Role Role { get; set; }
        #endregion
    }
}
