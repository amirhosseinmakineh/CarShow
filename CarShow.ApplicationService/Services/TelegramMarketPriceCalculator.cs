using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public sealed record TelegramMarketPriceGroup(
    IReadOnlyList<CarListing> Observations,
    IReadOnlyList<CarListing> Samples,
    IReadOnlyList<CarListing> Duplicates,
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
        if (string.IsNullOrWhiteSpace(listing.Manufacturer) || string.IsNullOrWhiteSpace(listing.Model)
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

            var samples = originals.Values.OrderBy(listing => listing.Id).ToArray();
            var average = ReferencePrice(samples.Select(listing => listing.NormalizedPrice!.Value));
            yield return new TelegramMarketPriceGroup(originals.Values.ToArray(), samples, duplicates, average);
        }
    }

    public static decimal ReferencePrice(IEnumerable<decimal> prices)
    {
        var ordered = prices.OrderBy(price => price).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one valid price is required.", nameof(prices));
        var middle = ordered.Length / 2;
        return decimal.Round(ordered.Length % 2 == 1 ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2m, 2, MidpointRounding.AwayFromZero);
    }
}
