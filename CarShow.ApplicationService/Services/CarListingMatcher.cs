using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public static class CarListingMatcher
{
    public static string BuildComparisonKey(string manufacturer, string model, string trim, int year, string yearCalendar, bool isUsed)
    {
        return string.Join("|", Normalize(manufacturer), Normalize(model), NormalizeTrim(trim), yearCalendar,
            year.ToString(CultureInfo.InvariantCulture), isUsed ? "used" : "new");
    }

    public static string NormalizeTrim(string value)
    {
        var trim = Normalize(value);
        trim = Regex.Replace(trim, @"\s+(?:قیمت|کارکرد|کیلومتر|مدل|سال)\s*[:：]?.*$", "").Trim();
        trim = trim.Replace("اتوماتیک", "اتومات", StringComparison.Ordinal)
            .Replace("دنده‌ای", "دنده ای", StringComparison.Ordinal);
        return Regex.Replace(trim, @"\s+", " ").Trim();
    }

    public static string Normalize(string value)
    {
        var text = new StringBuilder();
        foreach (var ch in value.ToLowerInvariant())
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(ch);
            text.Append(digit >= 0 ? (char)('0' + digit) : ch switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                '\u200c' => ' ',
                _ => ch
            });
        }

        return Regex.Replace(text.ToString(), @"\s+", " ").Trim();
    }

    public static DateOnly TehranDay(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
    }

    public static void Match(CarListing listing, IReadOnlyList<Car> catalog)
    {
        listing.CarId = null;
        listing.NormalizedPrice = null;
        listing.MatchStatus = "Invalid";
        listing.ProcessingStatus = "Processing";
        listing.ValidatedAt = DateTime.UtcNow;
        listing.ReasonCode = null;
        listing.ComparisonKey ??= listing.Manufacturer is not null && listing.Model is not null && listing.Trim is not null
            && listing.Year is not null && listing.YearCalendar is not null && listing.IsUsed is not null
                ? BuildComparisonKey(listing.Manufacturer, listing.Model, listing.Trim, listing.Year.Value, listing.YearCalendar, listing.IsUsed.Value)
                : null;
        if (listing.Price is null or <= 0 || listing.Currency is not ("IRT" or "IRR"))
        {
            listing.MatchStatus = "NeedsReview";
            listing.MatchReason ??= "Price or explicit currency is missing, ambiguous, or invalid.";
            listing.ReasonCode ??= "INVALID_PRICE_OR_CURRENCY";
            listing.ProcessingStatus = "NeedsReview";
            return;
        }

        if (listing.IsUsed is null)
        {
            listing.MatchReason = "New/used condition must be explicit (zero mileage, used, or mileage).";
            listing.ReasonCode = "CONDITION_UNCLEAR";
            listing.MatchStatus = "NeedsReview";
            listing.ProcessingStatus = "NeedsReview";
            return;
        }

        listing.NormalizedPrice = listing.Currency == "IRR" ? listing.Price / 10m : listing.Price;

        if (listing.ComparisonKey is null)
        {
            listing.MatchStatus = "NeedsReview";
            listing.MatchReason = "Company, model, trim, year, or condition is not fully verified.";
            listing.ReasonCode = "IDENTITY_INCOMPLETE";
            listing.ProcessingStatus = "NeedsReview";
            return;
        }

        var normalizedText = Normalize(listing.RawText);
        listing.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(listing.ComparisonKey + "|" + normalizedText)));

        var candidates = catalog.Where(car => !car.IsDelete
            && car.TelegramIdentityKey == listing.ComparisonKey
            && car.Year == listing.Year && car.IsUsed == listing.IsUsed).ToList();
        listing.MatchStatus = candidates.Count switch
        {
            0 => "Unmatched",
            1 => "Matched",
            _ => "Ambiguous"
        };
        listing.MatchReason = candidates.Count == 1
            ? "Exact company, model, trim, year, year calendar, and condition identity."
            : candidates.Count == 0 ? "No catalog vehicle exists for this verified identity yet."
            : $"{candidates.Count} catalog entries use the same verified identity.";
        listing.ReasonCode = candidates.Count == 1 ? null : candidates.Count == 0 ? "CATALOG_ENTRY_PENDING" : "IDENTITY_AMBIGUOUS";
        listing.ProcessingStatus = candidates.Count == 1 ? "WaitingForComparableChannels" : "NeedsReview";
        if (candidates.Count != 1)
        {
            return;
        }

        listing.CarId = candidates[0].Id;
        listing.Brand = candidates[0].Company.Name;
        listing.Model = candidates[0].CarModel.Name;
        listing.Trim = candidates[0].Tip?.Name;
    }

}
