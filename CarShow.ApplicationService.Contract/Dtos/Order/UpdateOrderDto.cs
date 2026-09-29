using CarShow.ApplicationService.Contract.Dtos.OrderItem;

namespace CarShow.ApplicationService.Contract.Dtos.Order
{
    public record UpdateOrderDto
    {
        public Guid UserId { get; set; }
        public long RoleId { get; set; }
        public long CarId { get; set; }
        public OrderItemDto OrderItem { get; set; }
    }
}
