using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderItemController : ControllerBase
    {
        private readonly IOrderItemService orderItemService;

        public OrderItemController(IOrderItemService orderItemService)
        {
            this.orderItemService = orderItemService;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderItems(long orderId)
        {
            var result = await orderItemService.GetOrderItems(orderId);
            return Ok(result);
        }
        [HttpGet("ConfirmOrderItem")]
        public async Task<IActionResult> ConfirmOrder(long orderItemId)
        {
            var result = await orderItemService.ConfirmOrder(orderItemId);
            return Ok(result);
        }

    }
}
