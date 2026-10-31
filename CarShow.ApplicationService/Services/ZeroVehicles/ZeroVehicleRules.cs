using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using Microsoft.Extensions.Configuration;
namespace CarShow.ApplicationService.Services.ZeroVehicles;

public sealed class ZeroVehicleOptions
{
    public int MaxPriceAgeDays { get; set; } = 7;
    public decimal MinimumPriceToman { get; set; } = 10_000_000m;
    public decimal MaximumPriceToman { get; set; } = 100_000_000_000m;
    public List<ZeroModelDefinition> Catalog { get; set; } = [];
    public static ZeroVehicleOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection("ZeroVehicles").Get<ZeroVehicleOptions>() ?? new();
        options.MaxPriceAgeDays = Math.Clamp(options.MaxPriceAgeDays, 1, 30);
        if (options.MinimumPriceToman <= 0 || options.MaximumPriceToman <= options.MinimumPriceToman || options.MaximumPriceToman > 9_999_999_999_999_999m)
            throw new InvalidOperationException("ZeroVehicles price bounds are invalid.");
        return options;
    }
}
public sealed class ZeroModelDefinition
{
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<ZeroTrimDefinition> Trims { get; set; } = [];
}
public sealed class ZeroTrimDefinition
{
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<List<string>> RequiredAliasGroups { get; set; } = [];
    public List<string> ExcludedAliases { get; set; } = [];
}
public sealed record ZeroValidationResult(ZeroVehicleListing? Listing, string? RejectionCode);

public static class ZeroVehicleRules
{
    public static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
            result.Append(c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c switch { 'ي' or 'ى' => 'ی', 'ك' => 'ک', '\u200c' or '\u00a0' => ' ', _ => char.ToLowerInvariant(c) });
        return Regex.Replace(result.ToString(), @"\s+", " ").Trim();
    }
    private static bool ContainsAlias(string text, string alias) => !string.IsNullOrWhiteSpace(alias) && Regex.IsMatch(text,
        @"(?<![\p{L}\p{N}])" + Regex.Escape(Normalize(alias)).Replace(@"\ ", @"\s+") + @"(?![\p{L}\p{N}])");
    private static string[] Aliases(string canonical, string aliases) => aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries).Prepend(canonical).Distinct().ToArray();

    // Channel signatures describe destinations (zero/used), not the advertised vehicle.
    // Only remove contact/link lines; a condition stated in the body must still be validated.
    private static string AdvertisementBody(string text)
    {
        text = Regex.Replace(text,
            @"(?im)^[\t ]*(?:@[a-z][a-z0-9_]{4,31}|https?://(?:www\.)?(?:t\.me|telegram\.me)/[a-z][a-z0-9_]{4,31})(?![a-z0-9_])[^\r\n]*$", "");
        // Forwarded captions can flatten the same navigation footer onto one line.
        text = Regex.Replace(text, @"(?i)@[a-z][a-z0-9_]{4,31}(?![a-z0-9_])[\t ]*(?:👉|👈|→)[\t ]*(?:کارکرده|صفر)(?![\p{L}])", " ");
        // Common captions join the insurance date, condition and price: '07صفر2950م'.
        return Regex.Replace(text, @"(?<=[0-9۰-۹٠-٩])(?=صفر)|(?<=صفر)(?=[0-9۰-۹٠-٩])", " ");
    }

    private const string PriceToken = @"(?<amount>-?[0-9][0-9,٬.٫]*)\s*(?<scale>میلیارد|میلیون|هزار|م(?![\p{L}]))?(?:\s*و\s*(?<remainder>[0-9,٬.٫]+)\s*(?<remainderScale>میلیون|هزار))?\s*(?<currency>تومان|تومن|ریال|irt|irr)?";

    public static string? ZeroFailure(string text)
    {
        text = Normalize(AdvertisementBody(text));
        // Reject any positive odometer, including '20 هزار کیلومتر' and decimal thousand values.
        var mileage = Regex.Matches(text, @"(?:کارکرد|کیلومتر|km)\s*[:：=]?\s*([0-9][0-9,٬.٫]*)\s*(هزار)?|([0-9][0-9,٬.٫]*)\s*(هزار)?\s*(?:کیلومتر|km)(?![\p{L}])");
        foreach (Match match in mileage)
        {
            var value = Number(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value);
            if (value is > 0) return "POSITIVE_MILEAGE";
        }
        if (Regex.IsMatch(text, @"کار\s*کرده|کارکرده|دست\s*دوم|صفر\s*نیست|غیر\s*صفر|بدون\s*کارکرد\s*نیست|کارکرد\s*دارد|صفر\s*نمی\s*باشد|صفر\s*نبوده")) return "CONTRADICTORY_CONDITION";
        // Accessory claims and idioms must never establish the vehicle's condition.
        var condition = Regex.Replace(text, @"(?:لاستیک|تایر|رنگ|خرج|هزینه|باتری|موتور|گیربکس)\s*(?:(?:ها|های|نو|کاملا|کاملاً|بدنه)\s*|[:：]\s*)*(?:صفر(?:\s*کیلومتر|\s*خشک)?|خشک)|صفر\s*تا\s*صد", " ");
        return Regex.IsMatch(condition, @"(?<![\p{L}\p{N}])(?:صفر\s*کیلومتر|صفر\s*خشک|صفر|0\s*کیلومتر|خشک|کار\s*نکرده|بدون\s*کارکرد)(?![\p{L}\p{N}])") ? null : "ZERO_NOT_CONFIRMED";
    }

    public static ZeroValidationResult Validate(TelegramCollectedMessage message, string channel,
        IReadOnlyList<ZeroVehicleModel> catalog, ZeroVehicleOptions options, DateTime now)
    {
        if (message.Text.Length > 8000 || message.ExternalMessageId.Length > 32 || string.IsNullOrWhiteSpace(message.ExternalMessageId)) return new(null, "MESSAGE_INVALID");
        var text = Normalize(AdvertisementBody(message.Text));
        if (Regex.IsMatch(text, @"حواله|پیش\s*فروش|پیشفروش|امتیاز\s*(?:ثبت\s*نام|خرید)")) return new(null, "ALLOCATION_OR_PREORDER");
        if (Regex.IsMatch(text, @"^[^\p{L}\p{N}]*خریدار|(?<![\p{L}])(?:خریدارم|می\s*خرم|می\s*خریم|خریداری\s*می\s*شود)(?![\p{L}])")) return new(null, "BUY_REQUEST");
        var zeroFailure = ZeroFailure(text);
        if (zeroFailure is not null) return new(null, zeroFailure);
        if (message.MessageDate == default || message.MessageDate > now.AddMinutes(5)) return new(null, "PUBLICATION_DATE_INVALID");
        var models = catalog.Where(model => Aliases(model.Name, model.Aliases).Any(alias => ContainsAlias(text, alias))).ToArray();
        models = models.Where(model => !models.Any(other => other.Id != model.Id && Normalize(other.Name).StartsWith(Normalize(model.Name) + " ", StringComparison.Ordinal))).ToArray();
        if (models.Length != 1) return new(null, "MODEL_UNKNOWN_OR_AMBIGUOUS");
        var model = models[0];
        var trimDefinitions = options.Catalog.FirstOrDefault(x => x.Name == model.Name)?.Trims;
        var trims = model.Trims.Where(trim => TrimConstraintsMatch(text, trimDefinitions?.FirstOrDefault(x => x.Name == trim.Name)))
            .Select(trim => new { Trim = trim, Matches = Aliases(trim.Name, trim.Aliases).Where(alias => ContainsAlias(text, alias)).Select(Normalize).ToArray() }).Where(x => x.Matches.Length > 0).ToArray();
        trims = trims.Where(item => !trims.Any(other => other.Trim.Id != item.Trim.Id && item.Matches.All(shorter => other.Matches.Any(longer => longer != shorter && ContainsAlias(longer, shorter))))).ToArray();
        if (trims.Length != 1) return new(null, "TRIM_UNKNOWN_OR_AMBIGUOUS");
        var priceMatches = Regex.Matches(text, @"(?:قیمت(?:\s+(?:نقدی|فروش|کل))?|price)\s*[:：=💰💵💶💸🔴🟢]*\s*" + PriceToken).ToArray();
        // Dealers often write '2045م مشهد' without a price label. Require an explicit
        // unit and exactly one amount; years, phone numbers and bare numbers cannot qualify.
        if (priceMatches.Length == 0 && !Regex.IsMatch(text, @"قیمت|price"))
            priceMatches = Regex.Matches(text, @"(?<![\p{L}\p{N}.,٬٫])" + PriceToken)
                .Where(match => match.Groups["scale"].Success || match.Groups["currency"].Success).ToArray();
        if (priceMatches.Length != 1 || Regex.IsMatch(text, @"توافقی|قیمت\s*تماس|پیش\s*پرداخت|اقساط|قسط|بیعانه|ودیعه")) return new(null, "PRICE_INVALID");
        var priceMatch = priceMatches[0];
        var amount = Number(priceMatch.Groups["amount"].Value);
        var scale = priceMatch.Groups["scale"].Value switch { "میلیارد" => 1_000_000_000m, "میلیون" or "م" => 1_000_000m, "هزار" => 1000m, _ => 1m };
        if (amount is null or <= 0 || amount > options.MaximumPriceToman * 10 / scale) return new(null, "PRICE_INVALID");
        var price = amount.Value * scale + (Number(priceMatch.Groups["remainder"].Value) ?? 0m) * (priceMatch.Groups["remainderScale"].Value == "میلیون" ? 1_000_000m : 1000m);
        var currency = priceMatch.Groups["currency"].Value;
        if (currency is "ریال" or "irr") price /= 10m;
        else if (currency.Length == 0 && scale == 1m) return new(null, "CURRENCY_MISSING");
        if (price < options.MinimumPriceToman || price > options.MaximumPriceToman) return new(null, "PRICE_OUT_OF_RANGE");
        var yearMatch = Regex.Match(text, @"(?:مدل|سال|year)\s*[:：]?\s*([0-9]{4})(?![0-9])");
        int? year = int.TryParse(yearMatch.Groups[1].Value, out var y) && (y is >= 1300 and <= 1500 or >= 1900 and <= 2100) ? y : null;
        var originalName = Aliases(model.Name, model.Aliases).Where(alias => ContainsAlias(text, alias)).OrderByDescending(alias => alias.Length).First() + " " + trims[0].Matches.OrderByDescending(alias => alias.Length).First();
        return new(new ZeroVehicleListing
        {
            ModelId = model.Id, TrimId = trims[0].Trim.Id, OriginalName = originalName[..Math.Min(originalName.Length, 256)],
            PriceToman = decimal.Round(price, 2), Year = year, Channel = channel, ExternalMessageId = message.ExternalMessageId,
            PublishedAt = DateTime.SpecifyKind(message.MessageDate, DateTimeKind.Utc), ReceivedAt = now,
            SourceUrl = message.SourceUrl is { Length: <= 2048 } ? message.SourceUrl : null, RawText = message.Text,
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        }, null);
    }
    private static decimal? Number(string value) => decimal.TryParse(value.Replace(",", "").Replace("٬", "").Replace("٫", "."), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static bool TrimConstraintsMatch(string text, ZeroTrimDefinition? definition) => definition is null ||
        (!definition.ExcludedAliases.Any(alias => ContainsAlias(text, alias)) &&
         definition.RequiredAliasGroups.All(group => group.Count > 0 && group.Any(alias => ContainsAlias(text, alias))));
    public static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one price is required.");
        return decimal.Round(ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2m, 2);
    }
}
