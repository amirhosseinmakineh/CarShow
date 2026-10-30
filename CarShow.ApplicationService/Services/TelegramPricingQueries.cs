using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

public sealed class TelegramPricingQueries(ITelegramProcessingStore store) : ITelegramPricingQueries
{
    public async Task<TelegramPipelineTimelineDto> PipelineTimelineAsync(CancellationToken cancellationToken)
    {
        var state = await store.PricingStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (state?.RunStartedAt is null || !await store.Channels.AnyAsync(channel => !channel.IsDelete, cancellationToken))
        {
            return new TelegramPipelineTimelineDto("NotStarted", null, null, 0, 0, 0, []);
        }

        var startedAt = state.RunStartedAt.Value;
        var end = state.RunCompletedAt ?? DateTime.UtcNow;
        var jobs = await store.Jobs.AsNoTracking()
            .Where(job => !job.Channel.IsDelete && job.CreatedAt >= startedAt && job.CreatedAt <= end)
            .Select(job => new { job.ChannelId, job.Status, job.StartedAt, job.CompletedAt })
            .ToListAsync(cancellationToken);
        var pending = jobs.Any(job => job.Status is TelegramCollectionJobStatus.Queued
            or TelegramCollectionJobStatus.Processing);
        var channels = jobs.GroupBy(job => job.ChannelId).ToArray();
        var completedChannels = channels.Count(channel => channel.Any(job => job.Status == TelegramCollectionJobStatus.Succeeded));
        var failedChannels = channels.Count(channel => !channel.Any(job => job.Status == TelegramCollectionJobStatus.Succeeded)
            && channel.Any(job => job.Status is TelegramCollectionJobStatus.Failed
                or TelegramCollectionJobStatus.AccessRequired));

        var received = await store.Messages.AsNoTracking()
            .Where(message => !message.IsDelete && !message.Channel.IsDelete
                && message.CollectedAt >= startedAt && message.CollectedAt <= end)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), At = group.Max(message => message.CollectedAt) })
            .SingleOrDefaultAsync(cancellationToken);
        var candidates = await (from listing in store.Listings.AsNoTracking()
            join message in store.Messages.AsNoTracking() on listing.RawMessageId equals (long?)message.Id
            where !listing.IsDelete && !message.IsDelete && !message.Channel.IsDelete
                && message.CollectedAt >= startedAt && message.CollectedAt <= end
            select listing)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Parsed = group.Count(listing => listing.MatchStatus != "Ignored"),
                ParsedAt = group.Max(listing => (DateTime?)listing.ExtractedAt),
                Validated = group.Count(listing => listing.ExtractionStatus == "Validated"),
                ValidatedAt = group.Max(listing => listing.ExtractionStatus == "Validated" ? listing.ValidatedAt : null),
                Compared = group.Count(listing => listing.ComparedAt != null),
                ComparedAt = group.Max(listing => listing.ComparedAt),
                Stored = group.Count(listing => listing.ProcessingStatus == "Published" && listing.StoredAt != null),
                StoredAt = group.Max(listing => listing.ProcessingStatus == "Published" ? listing.StoredAt : null)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return new TelegramPipelineTimelineDto("NotStarted", null, null, 0, 0, 0, []);
        }
        var finished = state.RunCompletedAt is not null;
        var collectionFinished = jobs.Count > 0 && !pending;
        var stages = new List<TelegramTimelineStageDto>
        {
            new("Queued", "Completed", startedAt, jobs.Count),
            new("Collection", collectionFinished ? failedChannels == channels.Length ? "Failed" : "Completed"
                : jobs.Count > 0 ? "InProgress" : "Waiting",
                jobs.Where(job => job.CompletedAt != null).Max(job => job?.CompletedAt)
                    ?? jobs.Where(job => job.StartedAt != null).Min(job => job?.StartedAt),
                completedChannels + failedChannels),
            new("Received", received is not null ? "Completed" : finished ? "NoData" : "Waiting",
                received?.At, received?.Count ?? 0),
            new("Parsed", candidates?.Parsed > 0 ? "Completed" : finished ? "NoData" : "Waiting",
                candidates?.ParsedAt, candidates?.Parsed ?? 0),
            new("Validated", candidates?.Validated > 0 ? "Completed" : finished ? "NoData" : "Waiting",
                candidates?.ValidatedAt, candidates?.Validated ?? 0),
            new("Pricing", finished ? candidates?.Compared > 0 ? "Completed" : "NoData"
                : collectionFinished ? "InProgress" : "Waiting",
                finished ? state.RunCompletedAt : null, candidates?.Compared ?? 0),
            new("Compared", candidates?.Compared > 0 ? "Completed" : finished ? "NoData" : "Waiting",
                candidates?.ComparedAt, candidates?.Compared ?? 0),
            new("Stored", candidates?.Stored > 0 ? "Completed" : finished ? "NoData" : "Waiting",
                candidates?.StoredAt, candidates?.Stored ?? 0),
            new("Finished", finished ? "Completed" : "Waiting",
                state.RunCompletedAt, candidates?.Stored ?? 0)
        };
        var status = !finished ? "InProgress" : failedChannels == 0 ? "Completed"
            : completedChannels == 0 ? "Failed" : "Partial";
        return new TelegramPipelineTimelineDto(status, startedAt, state.RunCompletedAt,
            channels.Length, completedChannels, failedChannels, stages);
    }

    public async Task<TelegramPricingStateDto?> StateAsync(CancellationToken cancellationToken)
    {
        if (!await store.Channels.AnyAsync(channel => !channel.IsDelete, cancellationToken))
        {
            return new TelegramPricingStateDto(null, null, null);
        }
        return await store.PricingStates.AsNoTracking()
            .Where(x => x.RunStartedAt != null)
            .Select(x => new TelegramPricingStateDto(x.LastSuccessAt, x.LastAttemptAt, x.LastError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TelegramObservationDto>> ObservationsAsync(string? status, int page, CancellationToken cancellationToken)
    {
        var registeredChannelCount = await store.Channels.CountAsync(channel => !channel.IsDelete, cancellationToken);
        var query = store.Listings.AsNoTracking().Where(x => !x.IsDelete
            && store.Channels.Any(channel => channel.Id == x.ChannelId && !channel.IsDelete));
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
                && x.Model != null
                && x.Year != null
                && x.IsUsed != null);
        }
        return await query.OrderByDescending(x => x.Id).Skip(Math.Clamp(page, 0, 100000) * 100).Take(100)
            .Select(x => new TelegramObservationDto(x.Id, x.CarId, x.RawMessageId, x.SourceChannel,
                x.SourceMessageId, x.SourceUrl, x.Price, x.Currency, x.NormalizedPrice,
                x.PublishedAt, x.ReceivedAt, x.PriceDay, x.MatchStatus, x.MatchReason, x.Manufacturer, x.PrimaryImageUrl,
                x.ComparisonStatus, x.ComparisonAveragePrice, x.ComparedChannelCount, x.ValidPriceSourceCount,
                x.ProcessingStatus, x.ReasonCode, x.Year, x.Mileage, x.IsUsed, x.Trim,
                x.Model, x.PriceText, x.YearCalendar, x.ComparisonReason, x.ExtractionVersion, registeredChannelCount,
                x.RawCarName, x.ExtractionStatus, x.NameValidationStatus, x.PriceValidationStatus))
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
        return await store.History.AsNoTracking().Where(x => x.PriceDay != null && !x.Car.IsDelete
                && (x.Car.TelegramIdentityKey == null || store.Listings.Any(listing => !listing.IsDelete
                    && store.Channels.Any(channel => channel.Id == listing.ChannelId && !channel.IsDelete)
                    && listing.CarId == x.CarId))
                && (carId == null || x.CarId == carId))
            .OrderByDescending(x => x.PriceDay).ThenBy(x => x.CarId).Skip(Math.Clamp(page, 0, 100000) * 100).Take(100)
            .Select(x => new DailyCarPriceDto(x.CarId, x.Car.Name, x.MarketPrice, x.FactoryPrice,
                x.PriceDay, x.SampleCount, x.CalculatedAt, x.IsFinal,
                x.IndependentSourceCount, x.ConfidenceLevel)).ToListAsync(cancellationToken);
    }
}
