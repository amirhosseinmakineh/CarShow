using CarShow.ApplicationService.Contract.Dtos.Order;
using CarShow.ApplicationService.Contract.Dtos.OrderItem;
using CarShow.ApplicationService.Contract.IService;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CarShow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] JsonElement body)
        {
            // ۱. ساختن DTO به صورت دستی
            var dto = new CreaateOrderDto();

            // پر کردن فیلدهای اصلی
            dto.UserId = GetGuidValue(body, "userId"); // استفاده از متد جدید برای Guid
            dto.CarId = GetIntValue(body, "carId");
            dto.CreateOrderDate = GetDateTimeValue(body, "createOrderDate");

            // ۲. پر کردن زیرمجموعه (OrderItem)
            // فرض می‌کنیم در JSON یک آبجکت به نام orderItem وجود دارد
            var orderItemElement = body.GetProperty("orderItem");

            dto.OrderItem = new OrderItemDto
            {
                CarPrice = GetDecimalValue(orderItemElement, "carPrice"),
                PrePayment = GetDecimalValue(orderItemElement, "prePayment"),
                Time = GetIntValue(orderItemElement, "time")
            };

            // ۳. فراخوانی سرویس
            var result = await _orderService.CreateOrderForUser(dto);
            return Ok(result);
        }

        private decimal GetDecimalValue(JsonElement body, string key)
        {
            var prop = body.GetProperty(key);

            return prop.ValueKind switch
            {
                JsonValueKind.Number => prop.GetDecimal(),

                JsonValueKind.String => decimal.Parse(
                    (prop.GetString() ?? "")
                        .Replace(",", "")
                        .Replace(".", "")   // مهم: برای 1.000.000.000
                        .Replace(" ", ""),
                    System.Globalization.CultureInfo.InvariantCulture
                ),

                _ => throw new Exception($"{key} value is invalid")
            };
        }

        private int GetIntValue(JsonElement body, string key)
        {
            var prop = body.GetProperty(key);

            return prop.ValueKind switch
            {
                JsonValueKind.Number => prop.GetInt32(),

                JsonValueKind.String => int.Parse(
                    (prop.GetString() ?? "")
                        .Replace(",", "")
                        .Replace(".", "")   // مهم
                        .Replace(" ", ""),
                    System.Globalization.CultureInfo.InvariantCulture
                ),

                _ => throw new Exception($"{key} value is invalid")
            };
        }
        private Guid GetGuidValue(JsonElement body, string key)
        {
            var prop = body.GetProperty(key);

            // اگر به صورت رشته فرستاده شده (که معمولا Guid همینطور است)
            if (prop.ValueKind == JsonValueKind.String)
            {
                return Guid.Parse(prop.GetString());
            }
            throw new Exception($"{key} is not a valid GUID");
        }
        [HttpGet]
        public async Task<IActionResult> Orders()
        {
            var result = await _orderService.GetOrders();
            return Ok(result);
        }
        [HttpGet("GetUserOrders")]
        public async Task<IActionResult> GetUserOrders(Guid userId)
        {
            var result =  await _orderService.GetUserOrders(userId);
            return Ok(result);
        }
        private DateTime GetDateTimeValue(JsonElement body, string key)
        {
            var prop = body.GetProperty(key);

            return prop.ValueKind switch
            {
                JsonValueKind.String => DateTime.Parse(
                    prop.GetString() ?? "",
                    System.Globalization.CultureInfo.InvariantCulture
                ),

                _ => throw new Exception($"{key} value is invalid")
            };
        }


    }
}
