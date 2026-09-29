using Carshow.Utilities.Convertor;
using CarShow.ApplicationService.Contract.Dtos.OrderItem;
using CarShow.ApplicationService.Contract.Dtos.User;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services
{
    public class OrderItemService : IOrderItemService
    {
        private readonly IBaseRepository<long, OrderItem> orderItemRepository;
        private readonly IBaseRepository<long, Order> orderRepository;
        public OrderItemService(IBaseRepository<long, OrderItem> orderItemRepository, IBaseRepository<long, Order> orderRepository)
        {
            this.orderItemRepository = orderItemRepository;
            this.orderRepository = orderRepository;
        }

        public async Task<Result<string>> ConfirmOrder(long orderItemId)
        {
            var orderItem =  orderItemRepository.GetAll()
                    .Where(x => x.Id == orderItemId).FirstOrDefault(); 
            if (orderItem is not null)
            {
                    orderItem.IsConfirm = true;
                    await orderItemRepository.Update(orderItem);
                    await orderItemRepository.SaveChanges();
                    return Result<string>.Success("تایید فاکتور با موفقیت انجام شد");

            }
            else
                return
                    Result<string>.Failure("فاکتوری یافت نشد");
        }
        public async Task<Result<List<OrderItemDto>>> GetOrderItems(long orderId)
        {
            var order = await orderRepository.GetById(orderId);

            if (order is null)
                return Result<List<OrderItemDto>>.Failure("سفارش یافت نشد");

            var result = await orderItemRepository.GetAll()
                .Where(x => x.OrderId == orderId)
                .Select(x => new OrderItemDto
                {
                    OrderItemId = x.Id,
                    CarPrice = x.CarPrice,
                    IsConfirm = x.IsConfirm,
                    PrePayment = x.PrePayment,
                    Price = x.Price,
                    Time = x.Time,
                    CreateOrderItemDate = x.CreateOrderItemDate
                })
                .ToListAsync();

            return Result<List<OrderItemDto>>.Success(result);
        }

    }
}