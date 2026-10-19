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
        @"(?:قیمت|price)\s*[:：]?\s*([0-9,٬]+(?:[.٫][0-9]+)?)\s*(میلیارد|میلیون|هزار)?\s*(تومان|ریال|تومن|IRR|IRT)?",
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
        var price = ParseDecimal(priceMatch.Groups[1].Value);
        if (price is null || price <= 0 || PriceRegex.Matches(text).Count != 1)
        {
            return null;
        }

        var multiplier = priceMatch.Groups[2].Value switch
        {
            "میلیارد" => 1_000_000_000m,
            "میلیون" => 1_000_000m,
            "هزار" => 1_000m,
            _ => 1m
        };
        if (price > 9_999_999_999_999_999.99m / multiplier)
        {
            return null;
        }
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
        var year = yearValue is >= 1300 and <= 2100 ? (int?)yearValue : null;
        var modelMatch = ModelRegex.Match(text);

        return new CarListing
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
            Model = modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : null,
            Price = price,
            Currency = currency,
            Mileage = mileage,
            Year = year,
            ExtractedAt = DateTime.UtcNow,
            DeduplicationKey = $"{channel.NormalizedIdentifier}:{message.ExternalMessageId}"
        };
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
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
