using CarShow.ApplicationService.Contract.Dtos.Telegram;

namespace CarShow.ApplicationService.Contract.IService;

public interface ITelegramPricingQueries
{
    Task<TelegramPricingStateDto?> StateAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TelegramObservationDto>> ObservationsAsync(string? status, int page, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyCarPriceDto>> PricesAsync(long? carId, int page, CancellationToken cancellationToken);
}
