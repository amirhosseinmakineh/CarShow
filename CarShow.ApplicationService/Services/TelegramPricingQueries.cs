using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

public sealed class TelegramPricingQueries(ITelegramProcessingStore store) : ITelegramPricingQueries
{
    public Task<TelegramPricingStateDto?> StateAsync(CancellationToken cancellationToken)
    {
        return store.PricingStates.AsNoTracking()
            .Select(x => new TelegramPricingStateDto(x.LastSuccessAt, x.LastAttemptAt, x.LastError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TelegramObservationDto>> ObservationsAsync(string? status, int page, CancellationToken cancellationToken)
    {
        var query = store.Listings.AsNoTracking().Where(x => !x.IsDelete);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.MatchStatus == status);
        }
        return await query.OrderByDescending(x => x.Id).Skip(Math.Clamp(page, 0, 100000) * 100).Take(100)
            .Select(x => new TelegramObservationDto(x.Id, x.CarId, x.RawMessageId, x.SourceChannel,
                x.SourceMessageId, x.SourceUrl, x.Price, x.Currency, x.NormalizedPrice,
                x.PublishedAt, x.ReceivedAt, x.PriceDay, x.MatchStatus, x.MatchReason, x.Manufacturer, x.PrimaryImageUrl,
                x.ComparisonStatus, x.ComparisonAveragePrice, x.ComparedChannelCount, x.ValidPriceSourceCount,
                x.ProcessingStatus, x.ReasonCode, x.Year, x.Mileage, x.IsUsed, x.Trim))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyCarPriceDto>> PricesAsync(long? carId, int page, CancellationToken cancellationToken)
    {
        return await store.History.AsNoTracking().Where(x => x.PriceDay != null && (carId == null || x.CarId == carId))
            .OrderByDescending(x => x.PriceDay).ThenBy(x => x.CarId).Skip(Math.Clamp(page, 0, 100000) * 100).Take(100)
            .Select(x => new DailyCarPriceDto(x.CarId, x.Car.Name, x.MarketPrice, x.FactoryPrice,
                x.PriceDay, x.SampleCount, x.CalculatedAt, x.IsFinal)).ToListAsync(cancellationToken);
    }
}
