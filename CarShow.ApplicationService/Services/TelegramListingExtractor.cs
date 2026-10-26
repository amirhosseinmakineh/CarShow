using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public interface ITelegramListingExtractor
{
    CarListing? Extract(TelegramChannel channel, TelegramRawMessage message);
}

public sealed class TelegramListingExtractor : ITelegramListingExtractor
{
    private static readonly Regex PriceRegex = new(
        @"(?:قیمت(?:\s+(?:فروش|نقدی|کل))?|price)\s*[:：=💰💵💶💸🔴🟢]*\s*([-+]?[0-9,٬]+(?:[.٫][0-9]+)?)\s*(میلیارد|میلیون|هزار)?\s*(تومان|ریال|تومن|IRR|IRT)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MileageRegex = new(
        @"(?:کارکرد|کیلومتر|km)\s*[:：]?\s*([0-9,٬]+)\s*(هزار)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(
        @"(?:مدل|سال|year)\s*[:：]?\s*([0-9]{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ModelRegex = new(
        @"(?:خودرو|ماشین|car)\s*[:：]\s*([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CarListing? Extract(TelegramChannel channel, TelegramRawMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MessageText))
        {
            return null;
        }

        var text = NormalizeDigits(message.MessageText).Replace("ي", "ی").Replace("ك", "ک").Replace("\u200c", " ");
        var priceMatch = PriceRegex.Match(text);
        var priceText = priceMatch.Success ? priceMatch.Groups[1].Value : null;
        var price = priceMatch.Success && PriceRegex.Matches(text).Count == 1 ? ParseDecimal(priceText ?? "") : null;
        var multiplier = priceMatch.Groups[2].Value switch
        {
            "میلیارد" => 1_000_000_000m,
            "میلیون" => 1_000_000m,
            "هزار" => 1_000m,
            _ => 1m
        };
        // Small bare values are common contact codes, not comparable car prices.
        if (price is <= 0 || (multiplier == 1m && price < 10_000m)
            || price > 9_999_999_999_999_999.99m / multiplier)
            price = null;
        else if (price is not null)
            price *= multiplier;
        var currency = priceMatch.Groups[3].Value.ToUpperInvariant() switch
        {
            "تومان" or "تومن" or "IRT" => "IRT",
            "ریال" or "IRR" => "IRR",
            _ => null
        };
        var mileageMatch = MileageRegex.Match(text);
        var mileageValue = ParseDecimal(mileageMatch.Groups[1].Value);
        if (mileageMatch.Groups[2].Success)
        {
            mileageValue *= 1000;
        }

        var mileage = mileageValue is >= 0 and <= int.MaxValue ? (int?)mileageValue : null;
        var yearValue = ParseDecimal(YearRegex.Match(text).Groups[1].Value);
        var year = yearValue is >= 1300 and <= 1500 or >= 1900 and <= 2100 ? (int?)yearValue : null;
        var yearCalendar = year is >= 1300 and <= 1500 ? "Jalali" : year is >= 1900 ? "Gregorian" : null;
        var modelMatch = ModelRegex.Match(text);
        var carName = modelMatch.Success ? CleanIdentityField(modelMatch.Groups[1].Value) : KnownCarName(text);
        var imageUrls = ParseImageUrls(message.MediaMetadata);
        var manufacturer = ExtractManufacturer(text);
        if (string.IsNullOrWhiteSpace(carName))
            return null;
        var trimMatch = Regex.Match(text, @"(?:تیپ|تریم)\s*[:：]\s*([^\r\n]+)");
        var trim = trimMatch.Success ? CleanIdentityField(trimMatch.Groups[1].Value) :
            string.Join(" ", new[] { "توربو", "اتومات", "اتوماتیک", "دنده ای", "دنده‌ای", "دستی", "هیبرید", "پلاس", "تیپ 2", "تیپ 5" }
                .Where(x => text.Contains(x, StringComparison.Ordinal) && (x != "اتومات" || !text.Contains("اتوماتیک", StringComparison.Ordinal))));
        if (mileage is null && Regex.IsMatch(text, @"(?:کارکرد\s*[:：]?\s*صفر|صفر\s*کیلومتر)")) mileage = 0;

        var normalizedModel = NormalizeModelName(carName, manufacturer);
        var normalizedTrim = string.IsNullOrWhiteSpace(trim) ? null : CarListingMatcher.NormalizeTrim(trim);
        var comparisonKey = manufacturer is not null && normalizedModel is not null
            && normalizedTrim is not null && year is not null && GetCondition(text, mileage) is not null
            ? CarListingMatcher.BuildComparisonKey(manufacturer, normalizedModel, normalizedTrim, year.Value, yearCalendar!, GetCondition(text, mileage)!.Value)
            : null;

        var listing = new CarListing
        {
            RawMessageId = message.Id,
            PublishedAt = message.MessageDate,
            ReceivedAt = message.CollectedAt,
            IsUsed = GetCondition(text, mileage),
            ChannelId = channel.Id,
            SourceChannel = channel.NormalizedIdentifier,
            SourceMessageId = message.ExternalMessageId,
            SourceUrl = message.SourceUrl,
            RawText = message.MessageText,
            Model = normalizedModel,
            Manufacturer = manufacturer,
            PrimaryImageUrl = imageUrls.FirstOrDefault(),
            ImageUrls = imageUrls.Count == 0 ? null : string.Join("\n", imageUrls),
            Trim = string.IsNullOrEmpty(trim) ? null : trim,
            Price = price,
            PriceText = priceText,
            Currency = currency,
            Mileage = mileage,
            Year = year,
            YearCalendar = yearCalendar,
            ComparisonKey = comparisonKey,
            ExtractedAt = DateTime.UtcNow,
            DeduplicationKey = $"{channel.NormalizedIdentifier}:{message.ExternalMessageId}"
        };
        if (listing.Price is null || listing.Currency is null)
        {
            listing.ReasonCode = priceMatch.Success ? "PRICE_UNIT_MISSING_OR_AMBIGUOUS" : "PRICE_MISSING";
            listing.MatchReason = priceMatch.Success
                ? "قیمت یا واحد پول روشن نیست و برای مقایسه قیمت قابل استفاده نیست."
                : "برای این پیام قیمت قابل استخراج پیدا نشد؛ آگهی برای بررسی نگهداری شد.";
        }
        else if (listing.ComparisonKey is null)
        {
            listing.ReasonCode = "IDENTITY_INCOMPLETE";
            listing.MatchReason = "شرکت، مدل، تیپ، سال یا وضعیت خودرو به‌طور قابل‌تأیید کامل نیست.";
        }
        return listing;
    }

    private static string? NormalizeModelName(string? model, string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var normalized = CarListingMatcher.Normalize(model);
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            var brand = CarListingMatcher.Normalize(manufacturer);
            if (normalized.StartsWith(brand + " ", StringComparison.Ordinal))
                normalized = normalized[(brand.Length + 1)..];
        }
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string CleanIdentityField(string value)
    {
        var stop = Regex.Match(value, @"\s+(?:قیمت|کارکرد|کیلومتر|مدل|سال)\s*[:：]?.*$");
        return (stop.Success ? value[..stop.Index] : value).Trim().Trim(',', '،', ';', '|');
    }

    private static List<string> ParseImageUrls(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return [];
        return metadata.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    private static string? ExtractManufacturer(string text)
    {
        string[] manufacturers = ["ایران خودرو", "سایپا", "پژو", "رنو", "کیا", "هیوندای", "تویوتا", "بنز", "بی ام و", "جک", "چری", "فونیکس", "ام وی ام", "کرمان موتور", "مدیران خودرو", "لاماری", "فردا موتور"];
        var normalizedText = CarListingMatcher.Normalize(text);
        return manufacturers.Where(x => Regex.IsMatch(normalizedText,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(CarListingMatcher.Normalize(x))}(?![\p{{L}}\p{{N}}])"))
            .OrderByDescending(x => x.Length).FirstOrDefault();
    }

    private static string? KnownCarName(string text)
    {
        // Prefer the longest known name; unknown names remain null for manual matching.
        string[] names = ["دنا پلاس", "پژو پارس", "پژو 207", "پژو 206", "پژو 405", "تارا", "شاهین", "کوییک", "ساینا", "سمند", "رانا پلاس", "رانا", "دنا", "تیبا", "پراید", "هایما S7", "هایما S5", "جک S5", "جک J4", "کی ام سی", "فیدلیتی", "دیگنیتی", "لاماری", "ریسپکت", "کمری", "کرولا", "توسان", "سانتافه", "اسپورتیج", "سراتو", "النترا"];
        var normalizedText = CarListingMatcher.Normalize(text);
        return names.Where(name => Regex.IsMatch(normalizedText,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(CarListingMatcher.Normalize(name))}(?![\p{{L}}\p{{N}}])"))
            .OrderByDescending(name => name.Length).FirstOrDefault();
    }

    private static bool? GetCondition(string text, int? mileage)
    {
        var isNew = mileage == 0 || Regex.IsMatch(text, @"\b(?:صفر|نو)\b");
        var isUsed = mileage > 0 || Regex.IsMatch(text, @"دست\s*دوم|کارکرده");
        if (isNew == isUsed)
        {
            return null;
        }
        return isUsed;
    }

    private static string NormalizeDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(character);
            builder.Append(digit >= 0 ? (char)('0' + digit) : character);
        }

        return builder.ToString();
    }

    private static decimal? ParseDecimal(string value)
    {
        var normalized = value.Replace(",", string.Empty)
            .Replace("٬", string.Empty)
            .Replace("٫", ".");
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
