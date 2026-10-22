using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CarShow.ApplicationService.Services;

public sealed class TelegramPricingService(
    ITelegramProcessingStore store,
    ITelegramListingExtractor extractor,
    ITelegramCollectionQueue queue,
    IConfiguration configuration,
    ILogger<TelegramPricingService> logger)
{
    public async Task ScheduleCollectionAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Telegram:CollectionIntervalMinutes", 15)));
        var due = DateTime.UtcNow - interval;
        var channels = await store.Channels.AsNoTracking()
            .Where(x => x.Enabled && !x.IsDelete && (x.LastAttemptAt == null || x.LastAttemptAt < due))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var id in channels)
        {
            try
            {
                await queue.EnqueueAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not queue Telegram channel {ChannelId}", id);
            }
        }
    }

    public async Task CalculateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await store.RunExclusiveAsync("TelegramDailyPricing", async () =>
        {
            var state = await GetStateAsync(cancellationToken);
            state.LastAttemptAt = nowUtc;
            var catalog = await store.Cars.Where(x => !x.IsDelete).ToListAsync(cancellationToken);
            await ProcessMessagesAsync(catalog, nowUtc, cancellationToken);
            await store.SaveAsync(cancellationToken);
            await CalculateDaysAsync(catalog, nowUtc, cancellationToken);
            state.LastSuccessAt = nowUtc;
            state.LastError = null;
        }, cancellationToken);
    }

    public async Task RecordFailureAsync(string error, CancellationToken cancellationToken)
    {
        await store.RunExclusiveAsync("TelegramDailyPricing", async () =>
        {
            var state = await GetStateAsync(cancellationToken);
            state.LastAttemptAt = DateTime.UtcNow;
            state.LastError = error;
        }, cancellationToken);
    }

    private async Task<TelegramPricingState> GetStateAsync(CancellationToken cancellationToken)
    {
        var state = await store.PricingStates.SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            state = new TelegramPricingState();
            store.Add(state);
        }
        return state;
    }

    private async Task ProcessMessagesAsync(IReadOnlyList<Car> catalog, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var messages = await store.Messages.Include(x => x.Channel)
            .Where(x => !x.IsDelete && !store.Listings.Any(l => l.RawMessageId == x.Id
                || (l.ChannelId == x.ChannelId && l.SourceMessageId == x.ExternalMessageId)))
            .OrderBy(x => x.Id).Take(2000).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            var listing = extractor.Extract(message.Channel, message) ?? new CarListing
            {
                ChannelId = message.ChannelId,
                SourceChannel = message.Channel.NormalizedIdentifier,
                SourceMessageId = message.ExternalMessageId,
                SourceUrl = message.SourceUrl,
                RawText = message.MessageText,
                DeduplicationKey = $"{message.ChannelId}:{message.ExternalMessageId}"
            };
            listing.RawMessageId = message.Id;
            listing.PublishedAt = message.MessageDate;
            listing.ReceivedAt = message.CollectedAt;
            PrepareListing(listing, catalog, nowUtc);
            store.Add(listing);
            message.ProcessingStatus = listing.MatchStatus;
            message.ProcessingError = listing.MatchStatus == "Matched" ? null : listing.MatchReason;
        }

        // Upgrade existing extracted listings without making a parallel observations table.
        var pending = await store.Listings.Where(x => x.MatchStatus == "Pending").ToListAsync(cancellationToken);
        foreach (var listing in pending)
        {
            var message = await store.Messages.FirstOrDefaultAsync(x => x.ChannelId == listing.ChannelId
                && x.ExternalMessageId == listing.SourceMessageId, cancellationToken);
            if (message is not null)
            {
                var parsed = extractor.Extract(message.Channel ?? await store.Channels.SingleAsync(x => x.Id == listing.ChannelId, cancellationToken), message);
                listing.IsUsed = parsed?.IsUsed;
                listing.RawMessageId = message.Id;
                listing.PublishedAt = message.MessageDate;
                listing.ReceivedAt = message.CollectedAt;
            }
            PrepareListing(listing, catalog, nowUtc);
            if (message is not null)
            {
                message.ProcessingStatus = listing.MatchStatus;
                message.ProcessingError = listing.MatchStatus == "Matched" ? null : listing.MatchReason;
            }
        }
    }

    private void PrepareListing(CarListing listing, IReadOnlyList<Car> catalog, DateTime nowUtc)
    {
        CarListingMatcher.Match(listing, catalog);
        if (listing.PublishedAt is null || listing.PublishedAt == default(DateTime) || listing.PublishedAt > nowUtc.AddMinutes(5))
        {
            listing.MatchStatus = "Invalid";
            listing.MatchReason = "Publication timestamp is missing or in the future.";
            return;
        }

        listing.PriceDay = CarListingMatcher.TehranDay(listing.PublishedAt.Value);
        if (listing.MatchStatus == "Matched" && listing.PriceDay.Value < OpenWindowStart(nowUtc))
        {
            listing.MatchStatus = "Late";
            listing.MatchReason = "Received after the configured finalization window; retained for audit.";
        }
    }

    private DateOnly OpenWindowStart(DateTime nowUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var scheduledTime = TimeOnly.TryParseExact(configuration["Telegram:DailyCalculationTimeTehran"] ?? "00:15",
            "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : new TimeOnly(0, 15);
        var day = DateOnly.FromDateTime(local);
        if (TimeOnly.FromDateTime(local) < scheduledTime)
        {
            day = day.AddDays(-1);
        }
        return day.AddDays(-Math.Clamp(configuration.GetValue("Telegram:LateArrivalDays", 2), 1, 30));
    }

    private async Task CalculateDaysAsync(IReadOnlyList<Car> catalog, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = OpenWindowStart(nowUtc);
        var observations = await store.Listings.Where(x => !x.IsDelete && x.MatchStatus == "Matched"
            && x.CarId != null && x.PriceDay >= cutoff && x.NormalizedPrice > 0).ToListAsync(cancellationToken);
        var histories = await store.History.Where(x => x.PriceDay != null && (!x.IsFinal || x.PriceDay >= cutoff))
            .ToListAsync(cancellationToken);
        foreach (var group in observations.GroupBy(x => new { x.CarId, x.PriceDay }))
        {
            var car = catalog.FirstOrDefault(x => x.Id == group.Key.CarId);
            if (car is null)
            {
                continue;
            }
            try
            {
                UpdateDay(car, group.Key.PriceDay!.Value, group.ToList(), histories, nowUtc, cutoff);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily pricing failed for car {CarId} on {PriceDay}", car.Id, group.Key.PriceDay);
                foreach (var observation in group)
                {
                    observation.MatchStatus = "CalculationError";
                    observation.MatchReason = "Daily calculation failed for this car; previous price retained.";
                }
            }
        }

        foreach (var history in histories.Where(x => x.PriceDay < cutoff))
        {
            history.IsFinal = true;
        }
    }

    private void UpdateDay(Car car, DateOnly day, IReadOnlyList<CarListing> observations,
        IReadOnlyList<CarPriceHistory> histories, DateTime nowUtc, DateOnly cutoff)
    {
        var history = histories.SingleOrDefault(x => x.CarId == car.Id && x.PriceDay == day);
        if (history?.IsFinal == true)
        {
            return;
        }
        var distinct = observations.GroupBy(x => x.Fingerprint ?? $"message:{x.ChannelId}:{x.SourceMessageId}")
            .Select(x => x.First()).ToList();
        var average = decimal.Round(distinct.Average(x => x.NormalizedPrice!.Value), 2, MidpointRounding.AwayFromZero);
        if (history is null)
        {
            history = new CarPriceHistory
            {
                CarId = car.Id,
                PriceDay = day,
                Date = nowUtc,
                FactoryPrice = car.FactoryPrice
            };
            store.Add(history);
        }
        history.MarketPrice = average;
        history.SampleCount = distinct.Count;
        history.CalculatedAt = nowUtc;
        history.IsFinal = day < cutoff;
        if (car.MarketPriceDay is null || car.MarketPriceDay <= day)
        {
            car.MarketPrice = average;
            car.MarketPriceDay = day;
            car.PriceSampleCount = distinct.Count;
            car.PriceCalculatedAt = nowUtc;
            car.LastUpdated = nowUtc;
        }
    }

}
