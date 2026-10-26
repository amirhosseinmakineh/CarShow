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
            catalog = await UpdateComparisonsAsync(nowUtc, cancellationToken);
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
            var listing = extractor.Extract(message.Channel, message);
            if (listing is null)
            {
                message.ProcessingStatus = "Ignored";
                message.ProcessingError = "پیام خام نگهداری شد، اما نام یک مدل خودرو برای ساخت Candidate قابل‌تأیید نبود.";
                continue;
            }
            listing.RawMessageId = message.Id;
            listing.PublishedAt = message.MessageDate;
            listing.ReceivedAt = message.CollectedAt;
            PrepareListing(listing, catalog, nowUtc);
            store.Add(listing);
            message.ProcessingStatus = listing.MatchStatus;
            message.ProcessingError = listing.MatchReason;
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
                message.ProcessingError = listing.MatchReason;
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
            listing.ReasonCode = "PUBLICATION_DATE_INVALID";
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

    private async Task<IReadOnlyList<Car>> UpdateComparisonsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var listings = await store.Listings.Where(x => !x.IsDelete && x.ComparisonKey != null
                && x.NormalizedPrice > 0 && x.Currency != null && x.IsUsed != null && x.Year != null
                && x.Manufacturer != null && x.Model != null && x.Trim != null)
            .OrderByDescending(x => x.PublishedAt).Take(10000).ToListAsync(cancellationToken);
        var rawById = new Dictionary<long, TelegramRawMessage>();
        foreach (var rawIds in listings.Where(x => x.RawMessageId.HasValue).Select(x => x.RawMessageId!.Value).Distinct().Chunk(1000))
        {
            var batch = rawIds.ToArray();
            var rawRows = await store.Messages.Where(x => batch.Contains(x.Id)).ToListAsync(cancellationToken);
            foreach (var raw in rawRows) rawById[raw.Id] = raw;
        }
        foreach (var listing in listings)
        {
            listing.ComparedChannelCount = 0;
            listing.ValidPriceSourceCount = 0;
            listing.ComparisonAveragePrice = null;
            listing.ComparisonStatus = "WaitingForComparableChannels";
            listing.ComparedAt = null;
        }

        // Same normalized ad text is one observation, even if copied into another channel.
        foreach (var duplicates in listings.Where(x => !string.IsNullOrWhiteSpace(x.Fingerprint))
                     .GroupBy(x => x.Fingerprint!, StringComparer.Ordinal))
        {
            foreach (var duplicate in duplicates.OrderBy(x => x.Id).Skip(1))
            {
                duplicate.MatchStatus = "Rejected";
                duplicate.ProcessingStatus = "Rejected";
                duplicate.ReasonCode = "DUPLICATE_AD";
                duplicate.MatchReason = "The same normalized advertisement was already received; this copy is excluded from comparison.";
                SetRawStatus(rawById, duplicate, "Rejected", duplicate.MatchReason);
            }
        }

        var eligible = listings.Where(x => x.MatchStatus != "Rejected" && x.PrimaryImageUrl is not null
                && (x.IsUsed != true || x.Mileage is > 0) && x.SourceUrl is not null
                && x.Price is > 0 && x.NormalizedPrice is > 0 && x.PriceDay >= OpenWindowStart(nowUtc)
                && x.PublishedAt <= nowUtc.AddMinutes(5))
            .ToList();

        foreach (var listing in listings.Except(eligible))
        {
            if (listing.MatchStatus == "Rejected") continue;
            if (listing.PriceDay is not null && listing.PriceDay < OpenWindowStart(nowUtc))
            {
                listing.MatchStatus = "Late";
                listing.ProcessingStatus = "Late";
                listing.ReasonCode = "OUTSIDE_PRICE_WINDOW";
                listing.MatchReason = "Received outside the configured price calculation window; retained for audit.";
                SetRawStatus(rawById, listing, "Late", listing.MatchReason);
                continue;
            }
            listing.MatchStatus = "NeedsReview";
            listing.ProcessingStatus = "NeedsReview";
            listing.ComparisonStatus = "WaitingForComparableChannels";
            listing.ReasonCode = listing.PrimaryImageUrl is null ? "MISSING_VERIFIABLE_IMAGE"
                : listing.IsUsed == true && listing.Mileage is null ? "MISSING_MILEAGE"
                : listing.SourceUrl is null ? "MISSING_SOURCE_LINK"
                : "COMPARISON_FIELDS_INCOMPLETE";
            listing.MatchReason = listing.ReasonCode switch
            {
                "MISSING_VERIFIABLE_IMAGE" => "No verifiable original image was available from this public post.",
                "MISSING_MILEAGE" => "Used-car mileage is missing, so this listing cannot enter market pricing.",
                "MISSING_SOURCE_LINK" => "The original Telegram message link is missing.",
                _ => "A required identity, condition, or valid price field is incomplete."
            };
            listing.ComparisonReason = listing.MatchReason;
            SetRawStatus(rawById, listing, "NeedsReview", listing.MatchReason);
        }

        foreach (var group in eligible.GroupBy(x => x.ComparisonKey!, StringComparer.Ordinal))
        {
            var channelSamples = group.GroupBy(x => x.ChannelId)
                .Select(g => g.OrderByDescending(x => x.PublishedAt).First()).ToList();
            foreach (var listing in group)
            {
                var peers = channelSamples.Where(x => x.ChannelId != listing.ChannelId).Take(5).ToList();
                listing.ComparedChannelCount = peers.Count;
                listing.ValidPriceSourceCount = peers.Count + 1;
                listing.ComparisonAveragePrice = null;
                listing.ComparisonStatus = peers.Count >= 5 ? "Compared" : "WaitingForComparableChannels";
                listing.ComparedAt = peers.Count >= 5 ? nowUtc : null;
                listing.ComparisonReason = peers.Count >= 5
                    ? "Compared against five other independent public channels using verified identity, year, condition, and normalized prices."
                    : $"اطلاعات معتبر از {peers.Count} کانال دیگر موجود است؛ برای مقایسه نهایی {5 - peers.Count} کانال دیگر لازم است.";
                if (peers.Count < 5)
                {
                    listing.MatchStatus = "WaitingForComparableChannels";
                    listing.ProcessingStatus = "WaitingForComparableChannels";
                    listing.ReasonCode = "INSUFFICIENT_INDEPENDENT_CHANNELS";
                }
                SetRawStatus(rawById, listing, listing.ProcessingStatus, listing.ComparisonReason);
            }

            if (channelSamples.Select(x => x.ChannelId).Distinct().Count() < 6)
                continue;

            var representative = channelSamples.OrderByDescending(x => x.PublishedAt).First();
            var car = await UpsertTelegramCarAsync(representative, group.Key, nowUtc, cancellationToken);
            await store.SaveAsync(cancellationToken);
            foreach (var listing in group.Where(x => x.ComparedChannelCount >= 5))
            {
                var peers = channelSamples.Where(x => x.ChannelId != listing.ChannelId).Take(5).ToList();
                var compared = peers.Append(listing).ToList();
                listing.CarId = car.Id;
                listing.MatchStatus = "Matched";
                listing.MatchReason = "Stored after comparison with five independent channels.";
                listing.ReasonCode = null;
                listing.ComparisonAveragePrice = decimal.Round(compared.Average(x => x.NormalizedPrice!.Value), 2, MidpointRounding.AwayFromZero);
                listing.ProcessingStatus = "Published";
                listing.StoredAt = nowUtc;
                SetRawStatus(rawById, listing, "Stored", null);
            }
        }

        return await store.Cars.Where(x => !x.IsDelete).ToListAsync(cancellationToken);
    }

    private static void SetRawStatus(IReadOnlyDictionary<long, TelegramRawMessage> rawById,
        CarListing listing, string status, string? error)
    {
        if (listing.RawMessageId is long rawId && rawById.TryGetValue(rawId, out var raw))
        {
            raw.ProcessingStatus = status;
            raw.ProcessingError = error;
        }
    }

    private async Task<Car> UpsertTelegramCarAsync(CarListing listing, string identityKey, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await store.Cars.FirstOrDefaultAsync(x => x.TelegramIdentityKey == identityKey, cancellationToken);
        if (existing is not null) return existing;

        var companyName = listing.Manufacturer!.Trim();
        var modelName = listing.Model!.Trim();
        var trimName = listing.Trim!.Trim();
        var company = await store.Companies.FirstOrDefaultAsync(x => !x.IsDelete && x.Name == companyName, cancellationToken);
        if (company is null)
        {
            company = new Company { Name = companyName, IsDelete = false };
            store.Add(company);
        }
        var model = await store.CarModels.FirstOrDefaultAsync(x => !x.IsDelete && x.Name == modelName, cancellationToken);
        if (model is null)
        {
            model = new CarModel { Name = modelName, IsDelete = false };
            store.Add(model);
        }
        var tip = await store.Tips.FirstOrDefaultAsync(x => !x.IsDelete && x.Name == trimName, cancellationToken);
        if (tip is null)
        {
            tip = new Tip { Name = trimName, IsDelete = false };
            store.Add(tip);
        }

        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identityKey)))[..16].ToLowerInvariant();
        var car = new Car
        {
            Company = company, CarModel = model, Tip = tip,
            CompanyId = company.Id, CarModelId = model.Id, TipId = tip.Id,
            IsUsed = listing.IsUsed!.Value, Year = listing.Year, YearCalendar = listing.YearCalendar,
            TelegramIdentityKey = identityKey,
            Name = $"{companyName} {modelName} {trimName} {listing.Year}",
            Slug = $"telegram-{digest}",
            ImageName = listing.PrimaryImageUrl!, SourceUrl = listing.SourceUrl!,
            LastUpdated = nowUtc, MarketPrice = 0, FactoryPrice = 0,
            PriceSampleCount = 0, IsDelete = false
        };
        store.Add(car);
        return car;
    }

    private async Task CalculateDaysAsync(IReadOnlyList<Car> catalog, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = OpenWindowStart(nowUtc);
        var observations = await store.Listings.Where(x => !x.IsDelete && x.MatchStatus == "Matched"
            && x.ComparisonStatus == "Compared" && x.ComparedChannelCount >= 5 && x.ValidPriceSourceCount >= 6
            && x.ComparisonKey != null && x.CarId != null && x.PriceDay >= cutoff && x.NormalizedPrice > 0)
            .ToListAsync(cancellationToken);
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
        var distinct = observations.GroupBy(x => x.ChannelId)
            .Select(x => x.OrderByDescending(y => y.PublishedAt).First())
            .GroupBy(x => x.Fingerprint ?? $"message:{x.ChannelId}:{x.SourceMessageId}")
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
