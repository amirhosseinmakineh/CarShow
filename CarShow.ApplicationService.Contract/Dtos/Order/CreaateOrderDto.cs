using CarShow.ApplicationService.Contract.Dtos.OrderItem;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CarShow.ApplicationService.Contract.Dtos.Order
{
    public class CreaateOrderDto
    {
        public Guid UserId { get; set; }
        public long RoleId { get; set; }
        public long CarId { get; set; }
        public DateTime CreateOrderDate { get; set; }
        public OrderItemDto OrderItem { get; set; }
    }
}
