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
        var registeredChannelCount = await store.Channels.CountAsync(channel => !channel.IsDelete, cancellationToken);
        var query = store.Listings.AsNoTracking().Where(x => !x.IsDelete);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.MatchStatus == status);
        }
        else
        {
            query = query.Where(x => x.MatchStatus == "Matched"
                && x.ProcessingStatus == "Published"
                && x.ComparisonStatus == "Compared"
                && x.NormalizedPrice > 0
                && x.SourceUrl != null
                && x.PrimaryImageUrl != null
                && x.Manufacturer != null
                && x.Model != null
                && x.Trim != null
                && x.Year != null
                && x.IsUsed != null);
        }
        return await query.OrderByDescending(x => x.Id).Skip(Math.Clamp(page, 0, 100000) * 100).Take(100)
            .Select(x => new TelegramObservationDto(x.Id, x.CarId, x.RawMessageId, x.SourceChannel,
                x.SourceMessageId, x.SourceUrl, x.Price, x.Currency, x.NormalizedPrice,
                x.PublishedAt, x.ReceivedAt, x.PriceDay, x.MatchStatus, x.MatchReason, x.Manufacturer, x.PrimaryImageUrl,
                x.ComparisonStatus, x.ComparisonAveragePrice, x.ComparedChannelCount, x.ValidPriceSourceCount,
                x.ProcessingStatus, x.ReasonCode, x.Year, x.Mileage, x.IsUsed, x.Trim,
                x.Model, x.PriceText, x.YearCalendar, x.ComparisonReason, x.ExtractionVersion, registeredChannelCount))
            .ToListAsync(cancellationToken);
    }

    public async Task<TelegramJobTimelineDto?> TimelineAsync(long jobId, CancellationToken cancellationToken)
    {
        var job = await store.Jobs.AsNoTracking().Where(item => item.Id == jobId)
            .Select(item => new
            {
                item.Id,
                item.ChannelId,
                item.Status,
                item.CreatedAt,
                item.StartedAt,
                item.CompletedAt,
                item.Error
            }).SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return null;
        }

        var end = job.CompletedAt ?? DateTime.UtcNow;
        var messages = job.StartedAt is null ? [] : await store.Messages.AsNoTracking()
            .Where(message => message.ChannelId == job.ChannelId
                && message.CollectedAt >= job.StartedAt && message.CollectedAt <= end)
            .Select(message => new { message.Id, message.CollectedAt }).ToArrayAsync(cancellationToken);
        var messageIds = messages.Select(message => message.Id).ToArray();
        var candidates = messageIds.Length == 0 ? [] : await store.Listings.AsNoTracking()
            .Where(listing => listing.RawMessageId != null
                && messageIds.Contains(listing.RawMessageId.Value))
            .Select(listing => new
            {
                listing.ExtractedAt,
                listing.ValidatedAt,
                listing.StoredAt,
                listing.ProcessingStatus,
                listing.MatchStatus
            }).ToArrayAsync(cancellationToken);
        var parsed = candidates.Where(candidate => candidate.MatchStatus != "Ignored").ToArray();
        var valid = parsed.Where(candidate => candidate.ProcessingStatus == "Published").ToArray();
        var stages = new List<TelegramTimelineStageDto>
        {
            new("Queued", "Completed", job.CreatedAt, 1),
            new("Collection", job.StartedAt is null ? "Waiting" : job.CompletedAt is null ? "InProgress" :
                job.Status.ToString() is "Failed" or "AccessRequired" ? "Failed" : "Completed", job.StartedAt, messageIds.Length),
            new("Received", messageIds.Length > 0 ? "Completed" : job.CompletedAt is null ? "Waiting" : "NoData",
                messages.Length > 0 ? messages.Max(message => message.CollectedAt) : null, messages.Length),
            new("Parsed", parsed.Length > 0 ? "Completed" : job.CompletedAt is null ? "Waiting" : "NoData",
                parsed.Length > 0 ? parsed.Max(candidate => candidate.ExtractedAt) : null, parsed.Length),
            new("Validated", parsed.Any(candidate => candidate.ValidatedAt != null) ? "Completed" :
                job.CompletedAt is null ? "Waiting" : "NoData",
                parsed.Where(candidate => candidate.ValidatedAt != null).Max(candidate => candidate?.ValidatedAt),
                parsed.Count(candidate => candidate.ValidatedAt != null)),
            new("Stored", valid.Length > 0 ? "Completed" : job.CompletedAt is null ? "Waiting" : "NoData",
                valid.Where(candidate => candidate.StoredAt != null).Max(candidate => candidate?.StoredAt), valid.Length)
        };
        return new TelegramJobTimelineDto(job.Id, job.ChannelId, job.Status.ToString(),
            job.CreatedAt, job.StartedAt, job.CompletedAt, job.Error, stages);
    }

    public async Task<IReadOnlyList<DailyCarPriceDto>> PricesAsync(long? carId, int page, CancellationToken cancellationToken)
    {
        return await store.History.AsNoTracking().Where(x => x.PriceDay != null && (carId == null || x.CarId == carId))
            .OrderByDescending(x => x.PriceDay).ThenBy(x => x.CarId).Skip(Math.Clamp(page, 0, 100000) * 100).Take(100)
            .Select(x => new DailyCarPriceDto(x.CarId, x.Car.Name, x.MarketPrice, x.FactoryPrice,
                x.PriceDay, x.SampleCount, x.CalculatedAt, x.IsFinal)).ToListAsync(cancellationToken);
    }
}
