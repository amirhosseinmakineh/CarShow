namespace CarShow.Domain.Models
{
    public class Order : BaseEntity<long>
    {
        public Guid UserId { get; set; }
        public long RoleId {  get; set; }
        public long CarId { get; set; }
        public string CreateOrderDate { get; set; } = string.Empty;
        #region Relations
        public User User { get; set; }
        public Role Role { get; set; }
        public Car Car { get; set; }
        public ICollection<OrderItem> OrderItems { get; set; }
        #endregion
    }
}
