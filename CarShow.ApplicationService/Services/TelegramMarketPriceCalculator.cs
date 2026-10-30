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
    public static (string Code, string Message)? ValidationFailure(CarListing listing, DateTime nowUtc, DateOnly cutoff)
    {
        if (listing.PriceDay is null || listing.PublishedAt is null || listing.PublishedAt > nowUtc.AddMinutes(5))
        {
            return ("PUBLICATION_DATE_INVALID", "تاریخ انتشار پیام معتبر نیست.");
        }
        if (listing.PriceDay < cutoff)
        {
            return ("OUTSIDE_PRICE_WINDOW", "پیام قدیمی است و در محاسبه قیمت روز وارد نمی‌شود.");
        }
        if (string.IsNullOrWhiteSpace(listing.Model)
            || listing.Year is null || listing.YearCalendar is null
            || listing.IsUsed is null || listing.ComparisonKey is null)
        {
            return ("COMPARISON_FIELDS_INCOMPLETE", "مشخصات لازم برای مقایسه خودرو کامل نیست.");
        }
        if (listing.Price is null or <= 0 || listing.NormalizedPrice is null or <= 0 || listing.Currency is not ("IRT" or "IRR"))
        {
            return ("PRICE_INVALID_OR_AMBIGUOUS", "قیمت و واحد پول معتبر برای مقایسه مشخص نیست.");
        }
        if (listing.IsUsed == true && listing.Mileage is null or <= 0)
        {
            return ("MISSING_MILEAGE", "کارکرد خودرو برای مقایسه مشخص نشده است.");
        }
        if (string.IsNullOrWhiteSpace(listing.SourceUrl))
        {
            return ("MISSING_SOURCE_LINK", "لینک پیام اصلی در دسترس نیست.");
        }
        if (listing.ReasonCode == "IDENTITY_AMBIGUOUS")
        {
            return ("IDENTITY_AMBIGUOUS", "بیش از یک خودرو با این مشخصات ثبت شده است.");
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(listing.RawText,
                @"فروخته\s*شد|فروش\s*رفت|sold", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return ("SOLD_AD", "این خودرو فروخته شده و در قیمت جاری مشارکت نمی‌کند.");
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
            // The median is only a robust reference for flagging extreme observations;
            // the published reference price is always the arithmetic mean.
            var ordered = channelSamples.Select(listing => listing.NormalizedPrice!.Value).Order().ToArray();
            var middle = ordered.Length / 2;
            var median = ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2m;
            var outliers = channelSamples.Length >= 3
                ? channelSamples.Where(listing => listing.NormalizedPrice < median / 2m
                    || listing.NormalizedPrice > median * 2m).ToArray() : [];
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
