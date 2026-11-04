using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models.Telegram;
using Microsoft.Extensions.Configuration;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.ZeroVehicles;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

public interface ITelegramListingExtractor
{
    Task LoadCatalogAsync(CancellationToken ct) => Task.CompletedTask;
    CarListing? Extract(TelegramChannel channel, TelegramRawMessage message);
}

public sealed class TelegramListingExtractor : ITelegramListingExtractor
{
    private TelegramVehicleNames vehicleNames;
    private readonly IConfiguration? configuration;
    private readonly IZeroVehicleStore? catalogStore;
    private readonly ITelegramProcessingStore? standardStore;
    private Dictionary<long, string> companies = [];
    private IReadOnlyList<ZeroVehicleModel> catalog = [];

    public TelegramListingExtractor(IConfiguration? configuration = null, IZeroVehicleStore? catalogStore = null, ITelegramProcessingStore? standardStore = null)
    {
        this.configuration = configuration; this.catalogStore = catalogStore; this.standardStore = standardStore;
        vehicleNames = new TelegramVehicleNames(configuration);
    }
    public async Task LoadCatalogAsync(CancellationToken ct)
    {
        if (catalogStore is null) return;
        catalog = await catalogStore.Models.AsNoTracking().Include(x => x.Trims).ToListAsync(ct);
        if (standardStore is not null) companies = await standardStore.Companies.AsNoTracking().Where(x => !x.IsDelete).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        vehicleNames = new TelegramVehicleNames(configuration, catalog);
    }
    public const int CurrentVersion = 7;
    private static readonly Regex PriceRegex = new(
        @"(?:قیمت(?:\s+(?:فروش|نقدی|کل))?|price)\s*[:：=💰💵💶💸🔴🟢]*\s*([-+]?(?:[0-9]{1,3}(?:[. \u00a0\u202f][0-9]{3}){2,}|[0-9,٬]+(?:[.٫][0-9]+)?))\s*(میلیارد|میلیون|هزار|م(?![\p{L}\p{N}]))?(?:\s*و\s*([0-9,٬]+)\s*(میلیون|هزار))?\s*(تومان|ریال|تومن|IRR|IRT)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MileageRegex = new(
        @"(?:کارکرد|کیلومتر|km)\s*[:：]?\s*([0-9,٬]+(?:[.٫][0-9]+)*)\s*(هزار)?(?![0-9,٬.٫])",
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
        var parsedSecondary = ParseDecimal(priceMatch.Groups[3].Value);
        var secondary = parsedSecondary ?? 0m;
        var secondaryMultiplier = priceMatch.Groups[4].Value == "میلیون" ? 1_000_000m : 1_000m;
        if (priceMatch.Groups[3].Success && parsedSecondary is null || secondary < 0
            || secondary > 9_999_999_999_999_999.99m / secondaryMultiplier
            || price is <= 0 || (multiplier == 1m && price < 10_000m)
            || price > 9_999_999_999_999_999.99m / multiplier)
            price = null;
        else if (price is not null)
        {
            price = price * multiplier + secondary * secondaryMultiplier;
            if (price > 9_999_999_999_999_999.99m) price = null;
        }
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
        var mileageText = Regex.Replace(text, @"بدون\s*کارکرد", " ");
        var mileageMatch = MileageRegex.Match(mileageText);
        if (!mileageMatch.Success) mileageMatch = Regex.Match(mileageText,
            @"(?<![0-9])([0-9,٬]+(?:[.٫][0-9]+)*)\s*(هزار)?\s*(?:کیلومتر|km)(?![\p{L}])", RegexOptions.IgnoreCase);
        var mileageValue = ParseDecimal(mileageMatch.Groups[1].Value);
        if (mileageMatch.Success && Regex.IsMatch(mileageText[(mileageMatch.Index + mileageMatch.Length)..],
            @"^\s*(?:میلیون|میلیارد|تومان|تومن|ریال|م(?![\p{L}]))")) mileageValue = null;
        var positiveMileage = mileageValue > 0;
        var ambiguousMileage = !mileageMatch.Groups[2].Success && Regex.IsMatch(mileageMatch.Groups[1].Value, @"^[0-9]{1,3}[.٫][0-9]{3}$");
        if (mileageMatch.Groups[2].Success)
        {
            mileageValue *= 1000;
        }

        var mileage = !ambiguousMileage && mileageValue is >= 0 and <= int.MaxValue && mileageValue % 1 == 0 ? (int?)mileageValue : null;
        var (year, yearCalendar, ambiguousYear) = VehicleModelYear.Read(text);
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
        if (string.IsNullOrWhiteSpace(carName) && !priceMatch.Success && !ambiguousName)
            return null;
        var trimMatch = Regex.Match(text, @"(?:تیپ|تریم)\s*[:：]\s*([^\r\n]+)");
        var trim = trimMatch.Success ? CleanIdentityField(trimMatch.Groups[1].Value) :
            string.Join(" ", new[] { "توربو", "اتومات", "اتوماتیک", "دنده ای", "دنده‌ای", "دستی", "هیبرید", "تیپ 2", "تیپ 5", "پانوراما", "هیدرولیک", "tu3", "tu5p", "tu5", "mc" }
                .Where(x => Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(x) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase) && (x != "اتومات" || !text.Contains("اتوماتیک", StringComparison.Ordinal))));
        if (mileage is null && Regex.IsMatch(text, @"(?:کارکرد\s*[:：]?\s*صفر|صفر\s*کیلومتر|بدون\s*کارکرد)", RegexOptions.IgnoreCase))
            mileage = 0;

        if (Regex.IsMatch(text, @"\bXU7\b", RegexOptions.IgnoreCase)
            && carName is not null && CarListingMatcher.Normalize(carName).Contains("پارس", StringComparison.Ordinal))
            trim = "XU7";

        if (carName == "شاهین" && Regex.IsMatch(text, @"(?<![\p{L}\p{N}])شاهین\s+G(?![\p{L}\p{N}])", RegexOptions.IgnoreCase))
            trim = "G";

        var automatic = Regex.IsMatch(text, @"اتومات(?:یک)?|automatic", RegexOptions.IgnoreCase);
        var manual = Regex.IsMatch(text, @"دنده\s*ای|دستی|manual", RegexOptions.IgnoreCase);
        var transmission = automatic && manual ? "Conflict" : automatic ? "Automatic" : manual ? "Manual" : null;
        var noPaint = Regex.IsMatch(text, @"بدون\s*رنگ|بی\s*رنگ");
        var fullPaint = Regex.IsMatch(text, @"تمام\s*رنگ|دور\s*رنگ");
        var bodyCondition = noPaint && fullPaint ? "Conflict" : noPaint ? "NoPaint" : fullPaint ? "FullPaint" : null;
        var standard = catalog.SingleOrDefault(x => CarListingMatcher.Normalize(x.Name) == CarListingMatcher.Normalize(carName ?? ""));
        if (standard is not null)
        {
            if (manufacturer is null && standard.CompanyId.HasValue) manufacturer = companies.GetValueOrDefault(standard.CompanyId.Value);
            var candidates = standard.Trims.Where(x => x.Name != "نام‌محور")
                .Select(x => new { Trim = x, Aliases = x.Aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries).Prepend(x.Name)
                    .Where(a => Regex.IsMatch(CarListingMatcher.Normalize(text), @"(?<![\p{L}\p{N}])" + Regex.Escape(CarListingMatcher.Normalize(a)) + @"(?![\p{L}\p{N}])")).ToArray() })
                .Where(x => x.Aliases.Length > 0).ToArray();
            candidates = candidates.Where(x => !candidates.Any(other => other.Trim.Id != x.Trim.Id
                && x.Aliases.All(a => other.Aliases.Any(b => b.Length > a.Length && CarListingMatcher.Normalize(b).Contains(CarListingMatcher.Normalize(a), StringComparison.Ordinal))))).ToArray();
            if (candidates.Length == 1)
            {
                var known = CarListingMatcher.NormalizeTrim(candidates[0].Trim.Name + " " + string.Join(" ", candidates[0].Aliases));
                var explicitTokens = CarListingMatcher.NormalizeTrim(trim ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (explicitTokens.All(token => known.Split(' ').Contains(token)))
                { trim = candidates[0].Trim.Name; transmission ??= candidates[0].Trim.Transmission; }
            }
            else if (candidates.Length > 1) transmission = "Conflict";
        }
        var normalizedModel = NormalizeModelName(carName, manufacturer);
        var normalizedTrim = string.IsNullOrWhiteSpace(trim) ? null : CarListingMatcher.NormalizeTrim(trim);
        var comparisonKey = normalizedModel is not null
            ? CarListingMatcher.BuildComparisonKey(manufacturer, normalizedModel, normalizedTrim, year,
                yearCalendar, GetCondition(text, mileage, positiveMileage), mileage, bodyCondition, transmission)
            : null;

        var identityTooLong = comparisonKey?.Length > 256 || normalizedTrim?.Length > 128 || manufacturer?.Length > 128;
        if (identityTooLong) { comparisonKey = null; normalizedModel = null; manufacturer = null; }
        var listing = new CarListing
        {
            ExtractionVersion = CurrentVersion,
            RawMessageId = message.Id,
            PublishedAt = message.MessageDate,
            ReceivedAt = message.CollectedAt,
            IsUsed = GetCondition(text, mileage, positiveMileage),
            ChannelId = channel.Id,
            SourceChannel = channel.NormalizedIdentifier,
            SourceMessageId = message.ExternalMessageId,
            SourceUrl = message.SourceUrl,
            RawText = message.MessageText,
            Model = normalizedModel,
            RawCarName = (rawCarName ?? explicitName) is { } rawName ? rawName[..Math.Min(rawName.Length, 128)] : null,
            NameValidationStatus = normalizedModel is null ? "NeedsReview" : "Validated",
            PriceValidationStatus = price is > 0 && currency is not null ? "Validated" : "NeedsReview",
            ExtractionStatus = normalizedModel is not null && price is > 0 && currency is not null ? "Validated" : "NeedsReview",
            Manufacturer = manufacturer,
            Brand = manufacturer,
            PrimaryImageUrl = imageUrls.FirstOrDefault(),
            ImageUrls = imageUrls.Count == 0 ? null : string.Join("\n", imageUrls),
            Trim = string.IsNullOrEmpty(trim) ? null : trim,
            Transmission = transmission,
            BodyCondition = bodyCondition,
            Price = price,
            PriceText = priceText is null ? null : priceText[..Math.Min(priceText.Length, 64)],
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
        if (identityTooLong) { listing.ReasonCode = "COMPARISON_IDENTITY_TOO_LONG"; listing.MatchReason = "مشخصات استخراج‌شده طول نامعتبر دارد و نیاز به بررسی است."; }
        if (ambiguousYear)
        {
            listing.ComparisonKey = null; listing.Model = null;
            listing.ReasonCode = "MODEL_YEAR_AMBIGUOUS"; listing.MatchReason = "بیش از یک سال مدل در آگهی دیده می‌شود.";
            listing.ExtractionStatus = "NeedsReview";
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

    private static bool? GetCondition(string text, int? mileage, bool positiveMileage)
    {
        text = Regex.Replace(text, @"(?:لاستیک|تایر|رنگ|خرج|هزینه|باتری|موتور|گیربکس)\s*(?:(?:ها|های|نو|کاملا|کاملاً|بدنه)\s*|[:：]\s*)*(?:صفر(?:\s*کیلومتر|\s*خشک)?|خشک)|صفر\s*تا\s*صد", " ");
        var isNew = mileage == 0 || Regex.IsMatch(text, @"(?:\bصفر\b|بدون\s*کارکرد|کارکرد\s*[:：]?\s*صفر|\bنو\b)", RegexOptions.IgnoreCase);
        var isUsed = mileage > 0 || positiveMileage || Regex.IsMatch(text, @"دست\s*دوم|کارکرده", RegexOptions.IgnoreCase);
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
