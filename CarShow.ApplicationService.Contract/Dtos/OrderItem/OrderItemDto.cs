using CarShow.ApplicationService.Contract.Dtos.Order;

namespace CarShow.ApplicationService.Contract.Dtos.OrderItem
{
    public record OrderItemDto
    {
        public long OrderItemId { get; set; }
        public decimal CarPrice { get; set; }
        public decimal PrePayment { get; set; }
        public int Time { get; set; }
        public decimal Price { get; set; }
        public bool IsConfirm { get; set; }
        public string CreateOrderItemDate { get; set; } = string.Empty;
    }
}
