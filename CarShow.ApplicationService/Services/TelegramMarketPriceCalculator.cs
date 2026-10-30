using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public sealed record TelegramMarketPriceGroup(
    IReadOnlyList<CarListing> Observations,
    IReadOnlyList<CarListing> Samples,
    IReadOnlyList<CarListing> Duplicates,
    IReadOnlyList<CarListing> Outliers,
    decimal Average);

/// <summary>Pure pricing rules: same identity/day, unique advertisements, one latest sample per channel.</summary>
public static class TelegramMarketPriceCalculator
{
    public static (string Code, string Message)? ValidationFailure(CarListing listing, DateTime nowUtc)
    {
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

    public static IEnumerable<TelegramMarketPriceGroup> Compare(IEnumerable<CarListing> eligible)
    {
        foreach (var group in eligible.GroupBy(listing => (listing.ComparisonKey, listing.PriceDay)))
        {
            var originals = new Dictionary<string, CarListing>(StringComparer.Ordinal);
            var duplicates = new List<CarListing>();
            foreach (var listing in group.OrderBy(listing => listing.Id))
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
            // Registration must preserve every listing with a recognized name and valid price.
            // Price outlier review must not prevent the car or its observed price from being stored.
            var outliers = Array.Empty<CarListing>();
            var excludedIds = outliers.Select(listing => listing.Id).ToHashSet();
            var samples = channelSamples.Where(listing => !excludedIds.Contains(listing.Id)).ToArray();
            var average = ReferencePrice(samples.Select(listing => listing.NormalizedPrice!.Value));
            yield return new TelegramMarketPriceGroup(originals.Values.ToArray(), samples, duplicates, outliers, average);
        }
    }

    public static decimal ReferencePrice(IEnumerable<decimal> prices)
    {
        var values = prices.ToArray();
        if (values.Length == 0 || values.Any(price => price <= 0))
            throw new ArgumentException("At least one positive price is required.", nameof(prices));
        return decimal.Round(values.Average(), 2, MidpointRounding.AwayFromZero);
    }
}
