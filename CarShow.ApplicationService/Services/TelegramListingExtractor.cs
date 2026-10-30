using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models.Telegram;
using Microsoft.Extensions.Configuration;

namespace CarShow.ApplicationService.Services;

public interface ITelegramListingExtractor
{
    CarListing? Extract(TelegramChannel channel, TelegramRawMessage message);
}

public sealed class TelegramListingExtractor : ITelegramListingExtractor
{
    private readonly TelegramVehicleNames vehicleNames;

    public TelegramListingExtractor(IConfiguration? configuration = null)
    {
        vehicleNames = new TelegramVehicleNames(configuration);
    }
    public const int CurrentVersion = 5;
    private static readonly Regex PriceRegex = new(
        @"(?:قیمت(?:\s+(?:فروش|نقدی|کل))?|price)\s*[:：=💰💵💶💸🔴🟢]*\s*([-+]?(?:[0-9]{1,3}(?:[. \u00a0\u202f][0-9]{3}){2,}|[0-9,٬]+(?:[.٫][0-9]+)?))\s*(میلیارد|میلیون|هزار|م(?![\p{L}\p{N}]))?(?:\s*و\s*([0-9,٬]+)\s*(میلیون|هزار))?\s*(تومان|ریال|تومن|IRR|IRT)?",
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
        text = Regex.Replace(text, @"(?<=[0-9])(?=مدل|سال|کارکرد|قیمت)", " ");
        var priceMatches = PriceRegex.Matches(text);
        var priceMatch = priceMatches.Count == 1 ? priceMatches[0] : Match.Empty;
        var priceText = priceMatch.Success ? priceMatch.Value : null;
        var price = priceMatch.Success ? ParseDecimal(priceMatch.Groups[1].Value) : null;
        var multiplier = priceMatch.Groups[2].Value switch
        {
            "میلیارد" => 1_000_000_000m,
            "میلیون" or "م" => 1_000_000m,
            "هزار" => 1_000m,
            _ => 1m
        };
        // Small bare values are common contact codes, not comparable car prices.
        var secondary = ParseDecimal(priceMatch.Groups[3].Value) ?? 0m;
        var secondaryMultiplier = priceMatch.Groups[4].Value == "میلیون" ? 1_000_000m : 1_000m;
        if (price is <= 0 || (multiplier == 1m && price < 10_000m)
            || price > 9_999_999_999_999_999.99m / multiplier)
            price = null;
        else if (price is not null)
            price = price * multiplier + secondary * secondaryMultiplier;
        var currency = priceMatch.Groups[5].Value.ToUpperInvariant() switch
        {
            "تومان" or "تومن" or "IRT" => "IRT",
            "ریال" or "IRR" => "IRR",
            // A price explicitly scaled as millions/billions is conventionally in toman.
            // A full amount labelled as a price uses the local toman convention.
            _ when multiplier >= 1_000_000m => "IRT",
            _ when price >= 100_000_000m => "IRT",
            _ => null
        };
        if (priceMatches.Count != 1 || Regex.IsMatch(text, @"توافقی|تماس بگیرید|قیمت در دایرکت", RegexOptions.IgnoreCase)
            || (Regex.IsMatch(text, @"پیش\s*پرداخت|اقساط|قسط", RegexOptions.IgnoreCase)
                && !priceMatch.Value.Contains("نقدی", StringComparison.Ordinal)))
        {
            price = null;
            currency = null;
        }
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
        var knownName = vehicleNames.FindUnique(text, out var ambiguousName, out var rawCarName);
        var explicitName = modelMatch.Success ? CleanIdentityField(modelMatch.Groups[1].Value) : null;
        // A long sentence following "خودرو:" is not a vehicle name (for example news).
        var carName = ambiguousName ? null : knownName;
        if (Regex.IsMatch(text, @"به دستور رئیس|رئیس جمهور|توقف واردات|واردات خودرو فعلا متوقف")
            && !Regex.IsMatch(text, @"فروش|کارکرد|صفر کیلومتر"))
            return null;
        var imageUrls = ParseImageUrls(message.MediaMetadata);
        var manufacturer = ExtractManufacturer(text);
        if (string.IsNullOrWhiteSpace(carName) && !priceMatch.Success)
            return null;
        var trimMatch = Regex.Match(text, @"(?:تیپ|تریم)\s*[:：]\s*([^\r\n]+)");
        var trim = trimMatch.Success ? CleanIdentityField(trimMatch.Groups[1].Value) :
            string.Join(" ", new[] { "توربو", "اتومات", "اتوماتیک", "دنده ای", "دنده‌ای", "دستی", "هیبرید", "تیپ 2", "تیپ 5" }
                .Where(x => text.Contains(x, StringComparison.Ordinal) && (x != "اتومات" || !text.Contains("اتوماتیک", StringComparison.Ordinal))));
        if (mileage is null && Regex.IsMatch(text, @"(?:کارکرد\s*[:：]?\s*صفر|صفر\s*کیلومتر|بدون\s*کارکرد)", RegexOptions.IgnoreCase))
            mileage = 0;

        if (Regex.IsMatch(text, @"\bXU7\b", RegexOptions.IgnoreCase)
            && carName is not null && CarListingMatcher.Normalize(carName).Contains("پارس", StringComparison.Ordinal))
            trim = "XU7";

        if (carName == "شاهین" && Regex.IsMatch(text, @"(?<![\p{L}\p{N}])شاهین\s+G(?![\p{L}\p{N}])", RegexOptions.IgnoreCase))
            trim = "G";

        var normalizedModel = NormalizeModelName(carName, manufacturer);
        var normalizedTrim = string.IsNullOrWhiteSpace(trim) ? null : CarListingMatcher.NormalizeTrim(trim);
        var comparisonKey = normalizedModel is not null
            ? CarListingMatcher.BuildComparisonKey(manufacturer, normalizedModel, normalizedTrim, year,
                yearCalendar, GetCondition(text, mileage), mileage)
            : null;

        var listing = new CarListing
        {
            ExtractionVersion = CurrentVersion,
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
            RawCarName = rawCarName ?? explicitName,
            NameValidationStatus = normalizedModel is null ? "NeedsReview" : "Validated",
            PriceValidationStatus = price is > 0 && currency is not null ? "Validated" : "NeedsReview",
            ExtractionStatus = normalizedModel is not null && price is > 0 && currency is not null ? "Validated" : "NeedsReview",
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
            listing.ReasonCode = !priceMatch.Success ? "PRICE_MISSING"
                : listing.Price is null ? "PRICE_AMBIGUOUS" : "CURRENCY_MISSING";
            listing.MatchReason = priceMatch.Success
                ? "قیمت یا واحد پول روشن نیست و برای مقایسه قیمت قابل استفاده نیست."
                : "برای این پیام قیمت قابل استخراج پیدا نشد؛ آگهی برای بررسی نگهداری شد.";
        }
        else if (listing.ComparisonKey is null)
        {
            listing.ReasonCode = ambiguousName ? "CAR_NAME_AMBIGUOUS" : "CAR_NAME_MISSING";
            listing.MatchReason = "نام خودرو به‌طور قابل‌تأیید مشخص نیست.";
        }
        return listing;
    }

    private static string? NormalizeModelName(string? model, string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var normalized = CarListingMatcher.Normalize(model);
        if (Regex.IsMatch(normalized, @"(?:پژو\s+)?پارس(?:\s+سال)?(?:\s+موتور)?\s+xu7\b"))
            normalized = "پژو پارس";
        // The canonical vehicle name is independent of whether the ad names
        // its manufacturer; BuildComparisonKey retains legacy catalog keys.
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
        if (Regex.IsMatch(normalizedText, @"(?<![\p{L}])(?:پارس\s+سال|پارس\s+موتور)\s+xu7\b"))
            return "پژو";
        return manufacturers.Where(x => Regex.IsMatch(normalizedText,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(CarListingMatcher.Normalize(x))}(?![\p{{L}}\p{{N}}])"))
            .OrderByDescending(x => x.Length).FirstOrDefault();
    }

    private static bool? GetCondition(string text, int? mileage)
    {
        var isNew = mileage == 0 || Regex.IsMatch(text, @"(?:\bصفر\b|بدون\s*کارکرد|کارکرد\s*[:：]?\s*صفر|\bنو\b)", RegexOptions.IgnoreCase);
        var isUsed = mileage > 0 || Regex.IsMatch(text, @"دست\s*دوم|کارکرده", RegexOptions.IgnoreCase);
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
        normalized = Regex.Replace(normalized, @"[ \u00a0\u202f]", "");
        if (Regex.IsMatch(normalized, @"^[-+]?[0-9]{1,3}(?:\.[0-9]{3}){2,}$"))
            normalized = normalized.Replace(".", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
