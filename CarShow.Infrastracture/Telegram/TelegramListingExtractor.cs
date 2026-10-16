using System.Globalization;
using System.Text.RegularExpressions;
using CarShow.Domain.Models.Telegram;

namespace CarShow.Infrastracture.Telegram;

public interface ITelegramListingExtractor
{
    CarListing? Extract(TelegramChannel channel, TelegramRawMessage message);
}

public sealed class TelegramListingExtractor : ITelegramListingExtractor
{
    private static readonly Regex PriceRegex = new(@"(?:قیمت|price)\s*[:：]?\s*([\d,.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MileageRegex = new(@"(?:کارکرد|کیلومتر|km)\s*[:：]?\s*([\d,.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"(?:مدل|سال|year)\s*[:：]?\s*(\d{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CarListing? Extract(TelegramChannel channel, TelegramRawMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MessageText)) return null;
        var price = ParseDecimal(PriceRegex.Match(message.MessageText).Groups[1].Value);
        if (price is null || price <= 0) return null;
        var mileage = ParseInt(MileageRegex.Match(message.MessageText).Groups[1].Value);
        var year = ParseInt(YearRegex.Match(message.MessageText).Groups[1].Value);
        return new CarListing
        {
            ChannelId = channel.Id,
            SourceChannel = channel.NormalizedIdentifier,
            SourceMessageId = message.ExternalMessageId,
            SourceUrl = message.SourceUrl,
            RawText = message.MessageText,
            Price = price,
            Currency = "IRR",
            Mileage = mileage,
            Year = year,
            ExtractedAt = DateTime.UtcNow,
            DeduplicationKey = $"{channel.NormalizedIdentifier}:{message.ExternalMessageId}"
        };
    }

    private static decimal? ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace(",", string.Empty).Replace(".", string.Empty).Replace("٬", string.Empty).Replace("٫", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static int? ParseInt(string value)
    {
        return int.TryParse(value.Replace(",", string.Empty).Replace(".", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
}
