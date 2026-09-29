using CarShow.ApplicationService.Contract.Dtos.OrderItem;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface IOrderItemService
    {
        Task<Result<List<OrderItemDto>>> GetOrderItems(long orderId);
        Task<Result<string>> ConfirmOrder(long orderItemId);

    }
}
