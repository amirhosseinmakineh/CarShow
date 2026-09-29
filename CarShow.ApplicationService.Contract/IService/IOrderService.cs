using CarShow.ApplicationService.Contract.Dtos.Order;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface IOrderService
    {
        Task<Result<List<OrderDto>>> GetOrders();
        Task<Result<object>> CreateOrderForUser(CreaateOrderDto dto);
        Task<Result<List<OrderDto>>> GetUserOrders(Guid userId);
    }
}
