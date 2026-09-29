using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CarShow.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries =
        [
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        ];
        private readonly IInstallmentCalculator _installmentCalculator;

        public WeatherForecastController(IInstallmentCalculator installmentCalculator)
        {
            _installmentCalculator = installmentCalculator;
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpPost("Calculate")]
        public IActionResult IInstallmentCalculator([FromBody] JsonElement body)
        {
            decimal carPrice = GetDecimalValue(body, "carPrice");
            decimal prePayment = GetDecimalValue(body, "prePayment");
            int time = GetIntValue(body, "time");

            var result =  _installmentCalculator.CalculateInstallment(carPrice, prePayment, time);

            return Ok(result);
        }

        private decimal GetDecimalValue(JsonElement body, string key)
        {
            var prop = body.GetProperty(key);

            switch (prop.ValueKind)
            {
                case JsonValueKind.Number:
                    return prop.GetDecimal();

                case JsonValueKind.String:
                    return decimal.Parse(
                        prop.GetString()
                            .Replace(",", "")
                            .Replace(" ", "")
                    );

                default:
                    throw new Exception($"{key} value is invalid");
            }
        }

        private int GetIntValue(JsonElement body, string key)
        {
            var prop = body.GetProperty(key);

            switch (prop.ValueKind)
            {
                case JsonValueKind.Number:
                    return prop.GetInt32();

                case JsonValueKind.String:
                    return int.Parse(
                        prop.GetString()
                            .Replace(",", "")
                            .Replace(" ", "")
                    );

                default:
                    throw new Exception($"{key} value is invalid");
            }
        }



    }
}
