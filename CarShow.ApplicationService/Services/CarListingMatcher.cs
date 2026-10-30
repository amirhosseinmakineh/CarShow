using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public static class CarListingMatcher
{
    public static string BuildComparisonKey(string? manufacturer, string model, string? trim, int year, string yearCalendar, bool isUsed, int? mileage = null)
    {
        var mileageRange = !isUsed ? "zero" : mileage switch
        {
            >= 1 and <= 50_000 => "1-50000",
            <= 100_000 and > 50_000 => "50001-100000",
            <= 200_000 and > 100_000 => "100001-200000",
            <= 300_000 and > 200_000 => "200001-300000",
            > 300_000 => "300001+",
            _ => "unknown"
        };
        // Preserve the existing key format for known vehicles so old cars and
        // histories remain reusable after the manufacturer becomes optional.
        var canonical = Normalize(model);
        var inferred = canonical.StartsWith("پژو ", StringComparison.Ordinal) ? "پژو"
            : canonical.StartsWith("دنا", StringComparison.Ordinal)
                || canonical.StartsWith("تارا", StringComparison.Ordinal)
                || canonical.StartsWith("سمند", StringComparison.Ordinal)
                || canonical.StartsWith("رانا", StringComparison.Ordinal)
                || canonical.StartsWith("هایما", StringComparison.Ordinal) ? "ایران خودرو"
            : canonical.StartsWith("kmc ", StringComparison.Ordinal) ? "کرمان موتور"
            : canonical.StartsWith("شاهین", StringComparison.Ordinal)
                || canonical.StartsWith("کوییک", StringComparison.Ordinal)
                || canonical.StartsWith("ساینا", StringComparison.Ordinal)
                || canonical.StartsWith("تیبا", StringComparison.Ordinal)
                || canonical.StartsWith("پراید", StringComparison.Ordinal) ? "سایپا"
            : canonical.StartsWith("جک ", StringComparison.Ordinal) ? "جک"
            : canonical.StartsWith("کمری", StringComparison.Ordinal)
                || canonical.StartsWith("کرولا", StringComparison.Ordinal) ? "تویوتا"
            : canonical.StartsWith("توسان", StringComparison.Ordinal)
                || canonical.StartsWith("سانتافه", StringComparison.Ordinal)
                || canonical.StartsWith("النترا", StringComparison.Ordinal) ? "هیوندای"
            : canonical.StartsWith("اسپورتیج", StringComparison.Ordinal)
                || canonical.StartsWith("سراتو", StringComparison.Ordinal) ? "کیا" : null;
        // Configured models without an established legacy brand use a neutral
        // component whether or not a manufacturer was mentioned in the ad.
        var brand = inferred ?? "?";
        var name = canonical.StartsWith(brand + " ", StringComparison.Ordinal)
            ? canonical[(brand.Length + 1)..] : canonical;
        var identity = string.Join("|", brand, name,
            string.IsNullOrWhiteSpace(trim) ? "?" : NormalizeTrim(trim), yearCalendar,
            year.ToString(CultureInfo.InvariantCulture), isUsed ? "used" : "new");
        // Keep existing new-car identities stable; used cars require the mileage band.
        return isUsed ? $"{identity}|{mileageRange}" : identity;
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
        listing.ComparisonKey ??= listing.Model is not null
            && listing.Year is not null && listing.YearCalendar is not null && listing.IsUsed is not null
                ? BuildComparisonKey(listing.Manufacturer, listing.Model, listing.Trim, listing.Year.Value,
                    listing.YearCalendar, listing.IsUsed.Value, listing.Mileage)
                : null;
        if (listing.Price is null or <= 0 || listing.Currency is not ("IRT" or "IRR"))
        {
            listing.MatchStatus = "NeedsReview";
            listing.ReasonCode ??= listing.Price is null or <= 0 ? "PRICE_MISSING_OR_INVALID" : "CURRENCY_MISSING";
            listing.MatchReason ??= listing.Price is null or <= 0
                ? "قیمت روشن و قابل استفاده از پیام استخراج نشده است."
                : "عدد قیمت وجود دارد، اما واحد تومان یا ریال مشخص نیست.";
            listing.ProcessingStatus = "NeedsReview";
            return;
        }

        var extractionReason = listing.ReasonCode;
        listing.ReasonCode = null;
        listing.NormalizedPrice = listing.Currency == "IRR" ? listing.Price / 10m : listing.Price;

        if (listing.IsUsed is null)
        {
            listing.MatchReason = "قیمت استخراج شده است، اما صفر یا کارکرده بودن خودرو روشن نیست.";
            listing.ReasonCode = "CONDITION_UNKNOWN";
            listing.MatchStatus = "NeedsReview";
            listing.ProcessingStatus = "NeedsReview";
            return;
        }

        if (listing.ComparisonKey is null)
        {
            listing.MatchStatus = "NeedsReview";
            listing.ReasonCode = extractionReason == "CAR_NAME_AMBIGUOUS" ? extractionReason
                : string.IsNullOrWhiteSpace(listing.Model) ? "CAR_NAME_MISSING"
                : "YEAR_MISSING";
            listing.MatchReason = listing.ReasonCode switch
            {
                "CAR_NAME_MISSING" => "نام خودرو مشخص نیست.",
                "CAR_NAME_AMBIGUOUS" => "بیش از یک خودروی متفاوت در پیام دیده می‌شود.",
                _ => "سال ساخت شمسی یا میلادی به‌طور روشن مشخص نشده است."
            };
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
        listing.Brand = candidates[0].Company?.Name;
        listing.Model = candidates[0].CarModel.Name;
        listing.Trim = candidates[0].Tip?.Name;
    }

}
