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
            var catalog = await store.Cars.ToListAsync(cancellationToken);
            await ProcessMessagesAsync(catalog.Where(car => !car.IsDelete).ToList(), nowUtc, cancellationToken);
            await store.SaveAsync(cancellationToken);
            catalog = (await UpdateComparisonsAsync(catalog, nowUtc, cancellationToken)).ToList();
            await store.SaveAsync(cancellationToken);
            await CalculateDaysAsync(catalog, nowUtc, cancellationToken);
            state.LastSuccessAt = nowUtc;
            state.LastError = null;
            if (state.RunStartedAt is not null && state.RunCompletedAt is null)
            {
                var pending = await store.Jobs.AnyAsync(job => job.CreatedAt >= state.RunStartedAt
                    && (job.Status == TelegramCollectionJobStatus.Queued
                        || job.Status == TelegramCollectionJobStatus.Processing), cancellationToken);
                if (!pending)
                {
                    state.RunCollectionCompletedAt = await store.Jobs
                        .Where(job => job.CreatedAt >= state.RunStartedAt && job.CompletedAt != null)
                        .MaxAsync(job => job.CompletedAt, cancellationToken);
                    state.RunCompletedAt = DateTime.UtcNow;
                }
            }
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
        var version = TelegramListingExtractor.CurrentVersion;
        var batchSize = Math.Clamp(configuration.GetValue("Telegram:ReprocessBatchSize", 2000), 1, 5000);
        var messages = await store.Messages.Include(x => x.Channel)
            .Where(x => !x.IsDelete && !x.Channel.IsDelete && (x.ExtractionVersion < version
                || x.ProcessingStatus == "Pending" || x.ProcessingStatus == "Extracted"))
            .OrderBy(x => x.Id).Take(batchSize).ToListAsync(cancellationToken);
        if (messages.Count == 0) return;
        var channels = messages.Select(x => x.ChannelId).Distinct().ToArray();
        var externalIds = messages.Select(x => x.ExternalMessageId).Distinct().ToArray();
        var existingRows = await store.Listings.Where(x => channels.Contains(x.ChannelId)
            && externalIds.Contains(x.SourceMessageId)).ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(x => (x.ChannelId, x.SourceMessageId));
        foreach (var message in messages)
        {
            existing.TryGetValue((message.ChannelId, message.ExternalMessageId), out var listing);
            message.ExtractionVersion = version;
            // Reprocessing repairs derived candidates, never previously published cars/history.
            if (listing is not null && (listing.IsDelete || listing.StoredAt != null || listing.ProcessingStatus == "Published"))
            {
                listing.ExtractionVersion = version;
                message.ProcessingStatus = listing.IsDelete ? "Ignored" : "Stored";
                message.ProcessingError = listing.IsDelete ? "آگهی قبلاً غیرفعال شده است." : null;
                continue;
            }
            var parsed = extractor.Extract(message.Channel, message);
            if (parsed is null)
            {
                message.ProcessingStatus = "Ignored";
                message.ProcessingError = "پیام خام نگهداری شد، اما آگهی خودرو قابل تشخیص نیست.";
                if (listing is not null)
                {
                    listing.ExtractionVersion = version;
                    listing.RawMessageId = message.Id;
                    listing.ValidatedAt = nowUtc;
                    listing.MatchStatus = listing.ProcessingStatus = "Ignored";
                    listing.ExtractionStatus = "Rejected";
                    listing.NameValidationStatus = "NeedsReview";
                    listing.PriceValidationStatus = "NeedsReview";
                    listing.RawCarName = null;
                    listing.ReasonCode = "NON_VEHICLE_MESSAGE";
                    listing.MatchReason = message.ProcessingError;
                    listing.Model = listing.Manufacturer = listing.Trim = null;
                    listing.Price = listing.NormalizedPrice = listing.ComparisonAveragePrice = null;
                    listing.ComparisonKey = listing.Fingerprint = listing.ComparisonStatus = null;
                    listing.ComparedChannelCount = listing.ValidPriceSourceCount = 0;
                }
                continue;
            }
            if (listing is null)
            {
                listing = parsed;
                store.Add(listing);
            }
            else
            {
                CopyExtraction(parsed, listing);
            }
            listing.ExtractionVersion = version;
            listing.RawMessageId = message.Id;
            listing.PublishedAt = message.MessageDate;
            listing.ReceivedAt = message.CollectedAt;
            PrepareListing(listing, catalog, nowUtc);
            message.ProcessingStatus = listing.ProcessingStatus;
            message.ProcessingError = listing.MatchReason;
        }
    }

    private static void CopyExtraction(CarListing source, CarListing target)
    {
        target.Model = source.Model;
        target.RawCarName = source.RawCarName;
        target.ExtractionStatus = source.ExtractionStatus;
        target.NameValidationStatus = source.NameValidationStatus;
        target.PriceValidationStatus = source.PriceValidationStatus;
        target.Brand = source.Brand;
        target.Manufacturer = source.Manufacturer;
        target.Trim = source.Trim;
        target.Year = source.Year;
        target.YearCalendar = source.YearCalendar;
        target.Mileage = source.Mileage;
        target.IsUsed = source.IsUsed;
        target.Price = source.Price;
        target.PriceText = source.PriceText;
        target.Currency = source.Currency;
        target.PrimaryImageUrl = source.PrimaryImageUrl;
        target.ImageUrls = source.ImageUrls;
        target.SourceUrl = source.SourceUrl;
        target.ComparisonKey = source.ComparisonKey;
        target.ExtractedAt = source.ExtractedAt;
        target.ReasonCode = source.ReasonCode;
        target.MatchReason = source.MatchReason;
        target.ComparedChannelCount = target.ValidPriceSourceCount = 0;
        target.ComparisonAveragePrice = null;
        target.ComparisonStatus = null;
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

    private async Task<IReadOnlyList<Car>> UpdateComparisonsAsync(IReadOnlyList<Car> existingCars, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = OpenWindowStart(nowUtc);
        var registeredChannels = store.Channels.Where(channel => !channel.IsDelete).Select(channel => channel.Id);
        var listings = await store.Listings
            .Where(listing => !listing.IsDelete && listing.ComparisonKey != null
                && listing.NormalizedPrice > 0 && listing.Currency != null
                && listing.Model != null && registeredChannels.Contains(listing.ChannelId))
            .ToListAsync(cancellationToken);
        var rawById = await LoadRawMessagesAsync(listings, cancellationToken);
        var eligible = new List<CarListing>(listings.Count);

        foreach (var listing in listings)
        {
            var reason = TelegramMarketPriceCalculator.ValidationFailure(listing, nowUtc);
            if (reason is not null)
            {
                MarkExcluded(listing, reason.Value.Code, reason.Value.Message, cutoff);
                SetRawStatus(rawById, listing, listing.ProcessingStatus, listing.MatchReason);
                continue;
            }

            eligible.Add(listing);
        }

        var catalog = await TelegramVehicleCatalog.LoadAsync(store, existingCars, cancellationToken);
        var removedIdentities = existingCars.Where(car => car.IsDelete && car.TelegramIdentityKey != null)
            .Select(car => car.TelegramIdentityKey!).ToHashSet(StringComparer.Ordinal);
        var assignments = new List<(TelegramMarketPriceGroup Group, Car Car)>();
        foreach (var group in TelegramMarketPriceCalculator.Compare(eligible))
        {
            foreach (var duplicate in group.Duplicates)
            {
                MarkExcluded(duplicate, "DUPLICATE_AD", "همین آگهی قبلاً دریافت شده است و دوباره وارد میانگین نمی‌شود.", cutoff);
                SetRawStatus(rawById, duplicate, duplicate.ProcessingStatus, duplicate.MatchReason);
            }

            foreach (var outlier in group.Outliers)
            {
                MarkExcluded(outlier, "PRICE_OUTLIER", "قیمت نسبت به نمونه‌های همین خودرو و روز غیرعادی است و نیاز به بررسی دارد.", cutoff);
                SetRawStatus(rawById, outlier, outlier.ProcessingStatus, outlier.MatchReason);
            }

            var representative = group.Samples[0];
            if (removedIdentities.Contains(representative.ComparisonKey!))
            {
                foreach (var listing in group.Observations)
                {
                    MarkExcluded(listing, "CAR_REMOVED", "این خودرو از سایت حذف شده است.", cutoff);
                }
                continue;
            }
            var car = catalog.GetOrCreate(representative, nowUtc);
            assignments.Add((group, car));
        }

        // One flush assigns generated vehicle IDs for every group, without a query/save per vehicle.
        await store.SaveAsync(cancellationToken);
        foreach (var (group, car) in assignments)
        {
            var sampleIds = group.Samples.Select(sample => sample.Id).ToHashSet();
            foreach (var listing in group.Observations.Except(group.Outliers))
            {
                var included = sampleIds.Contains(listing.Id);
                listing.CarId = car.Id;
                listing.ComparedChannelCount = group.Samples.Select(sample => sample.ChannelId).Distinct().Count() - 1;
                listing.ValidPriceSourceCount = group.Samples.Count;
                listing.ComparisonAveragePrice = group.Average;
                listing.ComparisonStatus = included ? "Compared" : "Superseded";
                listing.ComparisonReason = $"میانگین حسابی {group.Samples.Count} آگهی معتبر از {group.Samples.Select(sample => sample.ChannelId).Distinct().Count()} کانال برای همین خودرو و روز.";
                listing.ComparedAt = nowUtc;
                listing.MatchStatus = "Matched";
                listing.MatchReason = included ? "قیمت معتبر در میانگین روزانه وارد شد."
                    : "از این کانال آگهی معتبر جدیدتری برای همین خودرو و روز وجود دارد.";
                listing.ReasonCode = included ? null : "NEWER_CHANNEL_SAMPLE";
                listing.ProcessingStatus = included ? "Published" : "Stored";
                listing.StoredAt ??= nowUtc;
                SetRawStatus(rawById, listing, "Stored", included ? null : listing.MatchReason);
            }
        }

        return catalog.Cars;
    }

    private async Task<Dictionary<long, TelegramRawMessage>> LoadRawMessagesAsync(
        IReadOnlyList<CarListing> listings, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, TelegramRawMessage>();
        var ids = listings.Where(listing => listing.RawMessageId.HasValue)
            .Select(listing => listing.RawMessageId!.Value).Distinct();
        foreach (var chunk in ids.Chunk(1000))
        {
            var rows = await store.Messages.Where(message => chunk.Contains(message.Id)).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                result.Add(row.Id, row);
            }
        }

        return result;
    }

    private static void MarkExcluded(CarListing listing, string code, string reason, DateOnly cutoff)
    {
        var expired = listing.PriceDay < cutoff;
        listing.MatchStatus = expired ? "Late" : code == "DUPLICATE_AD" ? "Rejected" : "NeedsReview";
        listing.ProcessingStatus = listing.MatchStatus;
        listing.ReasonCode = code;
        listing.MatchReason = reason;
        listing.ComparisonStatus = "Excluded";
        listing.ComparisonReason = reason;
        listing.ComparedAt = null;
        listing.ComparedChannelCount = 0;
        listing.ValidPriceSourceCount = 0;
        listing.ComparisonAveragePrice = null;
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

    private async Task CalculateDaysAsync(IReadOnlyList<Car> catalog, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = OpenWindowStart(nowUtc);
        var observations = await store.Listings.Where(x => !x.IsDelete && x.Car != null && !x.Car.IsDelete && x.MatchStatus == "Matched"
            && x.ComparisonStatus == "Compared" && x.ValidPriceSourceCount > 0
            && x.ComparisonKey != null && x.CarId != null && x.NormalizedPrice > 0
            && store.Channels.Any(channel => channel.Id == x.ChannelId && !channel.IsDelete))
            .ToListAsync(cancellationToken);
        var histories = await store.History.Where(x => x.PriceDay != null && (!x.IsFinal || x.PriceDay >= cutoff))
            .ToListAsync(cancellationToken);
        var carsById = catalog.ToDictionary(car => car.Id);
        var historiesByDay = histories.ToDictionary(history => (history.CarId, history.PriceDay));
        foreach (var group in observations.GroupBy(x => new { x.CarId, x.PriceDay }).OrderBy(group => group.Key.PriceDay))
        {
            if (!carsById.TryGetValue(group.Key.CarId!.Value, out var car))
            {
                continue;
            }
            try
            {
                historiesByDay.TryGetValue((car.Id, group.Key.PriceDay), out var history);
                UpdateDay(car, group.Key.PriceDay!.Value, group.ToList(), history, nowUtc, cutoff);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily pricing failed for car {CarId} on {PriceDay}", car.Id, group.Key.PriceDay);
                foreach (var observation in group)
                {
                    observation.MatchStatus = "CalculationError";
                    observation.ProcessingStatus = "Failed";
                    observation.ReasonCode = "DAILY_CALCULATION_FAILED";
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
        CarPriceHistory? history, DateTime nowUtc, DateOnly cutoff)
    {
        if (history?.IsFinal == true)
        {
            return;
        }
        var distinct = observations.GroupBy(x => x.Fingerprint ?? $"message:{x.ChannelId}:{x.SourceMessageId}")
            .Select(x => x.First()).GroupBy(x => x.ChannelId)
            .Select(channel => channel.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).First()).ToList();
        var average = TelegramMarketPriceCalculator.ReferencePrice(distinct.Select(x => x.NormalizedPrice!.Value));
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
        history.IndependentSourceCount = distinct.Select(x => x.ChannelId).Distinct().Count();
        history.ConfidenceLevel = history.IndependentSourceCount == 1 ? "Low"
            : history.IndependentSourceCount <= 5 ? "Medium-Low"
            : history.IndependentSourceCount <= 10 ? "Medium" : "Higher";
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
