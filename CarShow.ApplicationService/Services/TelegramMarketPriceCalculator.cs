using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public sealed record TelegramMarketPriceGroup(
    IReadOnlyList<CarListing> Observations,
    IReadOnlyList<CarListing> Samples,
    IReadOnlyList<CarListing> Duplicates,
    IReadOnlyList<CarListing> Outliers,
    decimal Average);

/// <summary>Pure pricing rules: same identity/day, unique advertisements, one latest sample per channel.</summary>
public sealed record TelegramOutlierOptions(int MinimumSamples = 5, decimal MadMultiplier = 3.5m, decimal MinimumDeviationPercent = 25m);

public static class TelegramMarketPriceCalculator
{
    public static (string Code, string Message)? ValidationFailure(CarListing listing, DateTime nowUtc, decimal minimumToman = 10_000_000m, decimal maximumToman = 100_000_000_000m)
    {
        if (minimumToman <= 0 || maximumToman < minimumToman) throw new ArgumentOutOfRangeException(nameof(minimumToman));
        if (listing.NormalizedPrice is { } amount && (amount < minimumToman || amount > maximumToman))
            return ("PRICE_UNIT_OR_RANGE_REVIEW", "قیمت نرمال‌شده خارج از محدوده تنظیم‌شده است؛ واحد پول و مبلغ بررسی شود.");
        if (listing.AdminDecision == "Rejected") return ("ADMIN_REJECTED", "این آگهی توسط مدیر رد شده است.");
        if (listing.BodyCondition == "Conflict" || listing.Transmission == "Conflict")
            return ("CONTRADICTORY_SPECIFICATIONS", "مشخصات گیربکس یا بدنه متناقض است و نیاز به بررسی دارد.");
        if (listing.PriceDay is null || listing.PublishedAt is null || listing.PublishedAt > nowUtc.AddMinutes(5))
        {
            return ("PUBLICATION_DATE_INVALID", "تاریخ انتشار پیام معتبر نیست.");
        }
        if (string.IsNullOrWhiteSpace(listing.Model) || listing.ComparisonKey is null)
        {
            return ("COMPARISON_FIELDS_INCOMPLETE", "مشخصات لازم برای مقایسه خودرو کامل نیست.");
        }
        if (listing.Price is null or <= 0 || listing.NormalizedPrice is null or <= 0 || listing.Currency is not ("IRT" or "IRR"))
        {
            return ("PRICE_INVALID_OR_AMBIGUOUS", "قیمت و واحد پول معتبر برای مقایسه مشخص نیست.");
        }
        return null;
    }

    public static IEnumerable<TelegramMarketPriceGroup> Compare(IEnumerable<CarListing> eligible, TelegramOutlierOptions? options = null)
    {
        options ??= new();
        if (options.MinimumSamples < 5 || options.MadMultiplier <= 0 || options.MinimumDeviationPercent <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        foreach (var group in eligible.GroupBy(listing => (listing.ComparisonKey, listing.PriceDay)))
        {
            var originals = new Dictionary<string, CarListing>(StringComparer.Ordinal);
            var duplicates = new List<CarListing>();
            foreach (var listing in group.OrderByDescending(listing => listing.PublishedAt).ThenByDescending(listing => listing.Id))
            {
                var fingerprint = string.IsNullOrWhiteSpace(listing.Fingerprint)
                    ? $"message:{listing.ChannelId}:{listing.SourceMessageId}" : listing.Fingerprint;
                if (!originals.TryAdd(fingerprint, listing))
                {
                    duplicates.Add(listing);
                }
            }

            // A channel contributes at most one latest advertisement per vehicle/day.
            var channelSamples = originals.Values.GroupBy(listing => listing.ChannelId)
                .Select(channel => channel.OrderByDescending(listing => listing.PublishedAt)
                    .ThenByDescending(listing => listing.Id).First()).ToArray();
            // Screening affects publication, never storage of the observed price or raw message.
            // Small groups cannot establish a reliable statistical baseline.
            var outliers = Array.Empty<CarListing>();
            if (channelSamples.Length >= options.MinimumSamples)
            {
                var median = Median(channelSamples.Select(x => x.NormalizedPrice!.Value));
                var mad = Median(channelSamples.Select(x => Math.Abs(x.NormalizedPrice!.Value - median)));
                // 1.4826 scales MAD to sigma. The relative floor handles MAD=0 and tight clusters.
                var tolerance = Math.Max(options.MadMultiplier * 1.4826m * mad,
                    median * options.MinimumDeviationPercent / 100m);
                outliers = channelSamples.Where(x => x.AdminDecision != "Approved" && Math.Abs(x.NormalizedPrice!.Value - median) > tolerance).ToArray();
            }
            var excludedIds = outliers.Select(listing => listing.Id).ToHashSet();
            var samples = channelSamples.Where(listing => !excludedIds.Contains(listing.Id)).ToArray();
            var average = ReferencePrice(samples.Select(listing => listing.NormalizedPrice!.Value));
            yield return new TelegramMarketPriceGroup(originals.Values.ToArray(), samples, duplicates, outliers, average);
        }
    }

    private static decimal Median(IEnumerable<decimal> prices)
    {
        var values = prices.Order().ToArray();
        return values.Length % 2 == 1 ? values[values.Length / 2]
            : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2m;
    }

    public static decimal ReferencePrice(IEnumerable<decimal> prices)
    {
        var values = prices.ToArray();
        if (values.Length == 0 || values.Any(price => price <= 0))
            throw new ArgumentException("At least one positive price is required.", nameof(prices));
        return decimal.Round(values.Average(), 2, MidpointRounding.AwayFromZero);
    }
}
