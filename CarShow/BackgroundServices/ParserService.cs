using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static CarShow.BackgroundServices.TelegramScanService;

namespace CarShow.BackgroundServices
{
    internal static class ParserService
    {
        internal sealed record ArchivedVehicle(long? CarId, string Model, string? Trim, string? Year, string? Price, bool IsSale, string Evidence);
        internal sealed record ArchivePayload(int Version, string OriginalText, List<ArchivedVehicle> Vehicles);

        internal static ArchivePayload ExtractArchive(string message, IReadOnlyCollection<string> models,
            IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
        {
            var vehicles = new List<ArchivedVehicle>();
            var isSale = !Regex.IsMatch(NormalizeText(message), @"خریدار|خریداری|خواهان|حواله|پیش\s*فروش|معاوضه");
            var ads = ParseCatalog(message, models);
            var owners = MatchAdvertisementOwners(ads, catalog);
            for (var index = 0; index < ads.Count; index++)
            {
                var ad = ads[index];
                var matches = models.Where(model => CatalogContains(ad.Name, model)).Distinct().ToList();
                var specific = matches.Where(model => !matches.Any(other =>
                    CatalogNormalize(other) != CatalogNormalize(model) && CatalogContains(other, model))).ToList();
                if (specific.Count == 0) continue;
                long? id = owners.TryGetValue(index, out var owner) ? owner : null;
                var car = catalog.FirstOrDefault(x => x.Id == id);
                var modelName = id.HasValue ? car.Model : specific.Count == 1 ? specific[0] : ad.Name;
                vehicles.Add(new ArchivedVehicle(id, modelName, id.HasValue ? car.Tip : null,
                    ad.Year, ad.Price, isSale, ad.Name));
            }
            return new ArchivePayload(1, message, vehicles);
        }

        internal static string FormatArchive(ArchivePayload payload) =>
            string.Join("\n", payload.Vehicles.Select(x =>
                string.Join(" ", new[] { x.Model, x.Trim, x.Year }.Where(v => !string.IsNullOrWhiteSpace(v))) +
                (x.Price == null ? "" : " | " + x.Price) + (x.IsSale ? "" : " | غیر فروش")));

        internal static Dictionary<int, long> MatchAdvertisementOwners(
            IReadOnlyList<ParsedCar> advertisements,
            IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
        {
            var owners = new Dictionary<int, long>();
            var ambiguous = new HashSet<int>();
            var uniqueCars = catalog.GroupBy(x =>
                (Model: CatalogNormalize(x.Model), Tip: NormalizeTrimTerms(x.Tip)))
                .Where(g => g.Count() == 1).Select(g => g.Single()).ToList();

            foreach (var car in uniqueCars)
            {
                for (var i = 0; i < advertisements.Count; i++)
                {
                    var ad = advertisements[i];
                    if (!CatalogContains(ad.Name, car.Model) || !TrimContains(ad.Name, car.Tip))
                        continue;
                    if (catalog.Any(other => other.Id != car.Id &&
                        CatalogContains(ad.Name, other.Model) &&
                        TrimContains(ad.Name, other.Tip) &&
                        (IsStrictlyMoreSpecificTrim(other.Tip, car.Tip) ||
                         (CatalogNormalize(other.Model) != CatalogNormalize(car.Model) &&
                          CatalogContains(other.Model, car.Model)))))
                        continue;
                    if (owners.TryGetValue(i, out var previous) && previous != car.Id)
                        ambiguous.Add(i);
                    else
                        owners[i] = car.Id;
                }
            }
            foreach (var i in ambiguous) owners.Remove(i);
            return owners;
        }

        internal static string CatalogNormalize(string? value)
        {
            var text = (value ?? "").Replace('آ', 'ا').Replace('أ', 'ا').Replace('إ', 'ا')
                .Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ');
            text = new string(text.Select(c => c >= '۰' && c <= '۹' ? (char)('0' + c - '۰') :
                c >= '٠' && c <= '٩' ? (char)('0' + c - '٠') : c).ToArray());
            return Regex.Replace(Regex.Replace(text, @"[^\p{L}\p{N}]+", " "), @"\s+", " ").Trim().ToLowerInvariant();
        }

        // Normalize only well-established spelling variants of the SAME trim feature.
        // This is deliberately not fuzzy matching: unknown or conflicting trims remain rejected.
        internal static string NormalizeTrimTerms(string value)
        {
            var text = CatalogNormalize(value);
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:پانوراما|پانورامیک|پانا)(?![\p{L}\p{N}])", "پانوراما");
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:اتوماتیک|اتومات)(?![\p{L}\p{N}])", "اتومات");
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:دنده\s*ای|دنده)(?![\p{L}\p{N}])", "دنده");
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:ارتقاء|ارتقا\s*یافته|ارتقا)(?![\p{L}\p{N}])", "ارتقا");
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:تیو\s*تری|تیوتری|tu\s*3)(?![\p{L}\p{N}])", "tu3");
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:آپشنال|اپشنال|ابشنال)(?![\p{L}\p{N}])", "اپشنال");
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])(?:6\s*سرعته|6\s*دنده)(?![\p{L}\p{N}])", "6 دنده");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        // Every catalog Tip.Name generates its own regex; the catalog vocabulary is
        // never restricted to the hard-coded spelling aliases above.
        internal static bool TrimContains(string advertisement, string trim)
        {
            var value = NormalizeTrimTerms(advertisement);
            var token = NormalizeTrimTerms(trim);
            if (token.Length == 0) return false;
            var words = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var pattern = @"(?<![\p{L}\p{N}])" +
                string.Join(@"\s+", words.Select(Regex.Escape)) +
                @"(?![\p{L}\p{N}])";
            return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);
        }

        // Compare normalized trim feature tokens independent of word order.
        // Only a strict superset can eliminate a less-specific trim; equal token
        // sets (including duplicate catalog rows) remain ambiguous.
        internal static bool IsStrictlyMoreSpecificTrim(string candidate, string other)
        {
            var candidateTokens = NormalizeTrimTerms(candidate).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
            var otherTokens = NormalizeTrimTerms(other).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
            return candidateTokens.IsProperSupersetOf(otherTokens);
        }

        internal static bool CatalogContains(string advertisement, string term)
        {
            var value = CatalogNormalize(advertisement);
            var token = CatalogNormalize(term);
            return token.Length > 0 && (" " + value + " ").Contains(" " + token + " ", StringComparison.Ordinal);
        }

        // Match model first, then trim within that model. Ambiguity is never
        // resolved using string length: a wrong CarId would corrupt reference prices.
        public static long? MatchCatalogCar(string advertisement,
            IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
        {
            var text = CatalogNormalize(advertisement);
            if (text.Length == 0) return null;

            var models = catalog.Select(x => x.Model).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(model => CatalogContains(text, model))
                .OrderByDescending(model => CatalogNormalize(model).Length).ToList();
            if (models.Count == 0) return null;

            // A longer model may include a shorter model name (e.g. "207" vs "207 sd").
            // Only discard shorter names when they are fully contained in the longer one.
            var specificModels = models.Where(model => !models.Any(other =>
                !string.Equals(model, other, StringComparison.OrdinalIgnoreCase) &&
                CatalogContains(other, model))).ToList();
            if (specificModels.Count != 1) return null;

            var modelName = specificModels[0];
            var candidates = catalog.Where(item =>
                string.Equals(item.Model, modelName, StringComparison.OrdinalIgnoreCase) &&
                TrimContains(text, item.Tip)).ToList();

            // More specific trim wins only if all other matching trims are its
            // component phrases. Independent trim matches mean ambiguous ad.
            var specificTrims = candidates.Where(item => !candidates.Any(other =>
                item.Id != other.Id && IsStrictlyMoreSpecificTrim(other.Tip, item.Tip))).ToList();
            return specificTrims.Count == 1 ? specificTrims[0].Id : null;
        }

        // Canonical comparison key: 1405 / 405 / 05 => 2026.
        // This is a model-year convention, not a calendar-date conversion.
        internal static string NormalizeManufacturingYear(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var value = CatalogNormalize(raw).Trim();
            if (!Regex.IsMatch(value, @"^\d{2,4}$")) return null;
            if (!int.TryParse(value, out var year)) return null;
            if (year >= 2000 && year <= 2099) return year.ToString(CultureInfo.InvariantCulture);
            if (year >= 1300 && year <= 1399) return (year + 621).ToString(CultureInfo.InvariantCulture);
            if (year >= 1400 && year <= 1499) return (year + 621).ToString(CultureInfo.InvariantCulture);
            if (year >= 400 && year <= 499) return (year + 1621).ToString(CultureInfo.InvariantCulture);
            if (value.Length == 2 && year <= 30) return (2021 + year).ToString(CultureInfo.InvariantCulture);
            return null;
        }


            private static readonly Regex FullYearRegex = new Regex(
                @"(?<!\d)(?:13\d{2}|14\d{2}|20\d{2})(?!\d)",
                RegexOptions.Compiled);

            private static readonly Regex ShortYearRegex = new Regex(
                @"(?<!\d)(?<year>40[0-9])(?=\s*(?:روز|رووز|برج|ب\s*\d+|مدل))",
                RegexOptions.Compiled);

            private static readonly Regex PriceTokenRegex = new Regex(
                @"(?<!\d)(?:\d{1,3}(?:[.,/]\d{3,6})+|\d{4,8})(?!\d)",
                RegexOptions.Compiled);

            private static readonly Regex ExplicitPriceRegex = new Regex(
                @"(?<!\d)\d+(?:[.,/]\d{1,3})?\s*(?:میلیارد|میلیون)",
                RegexOptions.Compiled);

            // Colloquial Iranian car-ad prices: "4 تومن" means four billion in this context.
            private static readonly Regex ColloquialPriceRegex = new Regex(
                @"(?<![\p{L}\d])(?<amount>\d+(?:[.,/]\d{1,3})?)\s*(?:تومن|تومان)(?![\p{L}])",
                RegexOptions.Compiled);

            private static readonly Regex PhoneRegex = new Regex(
                @"09[\s\-]*\d{9}",
                RegexOptions.Compiled);

            private static readonly Regex MileageRegex = new Regex(
                @"کارکرد|کیلومتر|آمپر|هزار\s*تا",
                RegexOptions.Compiled);

            private static readonly Regex NoiseRegex = new Regex(
                @"(?<![\p{L}\p{N}])(?:قابل\s+تحویل|تحویل|سفید|مشکی|سیاه|" +
                @"خاکستری|طوسی|نقره\s*ای|ذغالی|کرم|قرمز|آبی|سبز|تهران|" +
                @"اصفهان|قم|پلاک|سند|آماده|گارانتی|خودرو)(?![\p{L}\p{N}])",
                RegexOptions.Compiled);

            public static List<ParsedCar> ParseCatalog(string message, IReadOnlyCollection<string> models)
            {
                var result = new List<ParsedCar>();
                if (string.IsNullOrWhiteSpace(message) || models.Count == 0) return result;
                var lines = NormalizeArchiveYears(message, models).Replace("\r", "").Split('\n');
                var starts = Enumerable.Range(0, lines.Length)
                    .Where(index => IsCatalogVehicleHeader(CleanLine(lines[index]), models))
                    .ToList();
                for (var i = 0; i < starts.Count; i++)
                {
                    var start = starts[i];
                    var end = i + 1 < starts.Count ? starts[i + 1] : lines.Length;
                    var block = string.Join("\n", lines.Skip(start).Take(end - start));
                    result.Add(new ParsedCar
                    {
                        Name = ExtractName(CleanLine(lines[start])),
                        Price = ExtractPrice(block),
                        Year = ExtractYearCatalog(block, lines[start])
                    });
                }
                return result;
            }

            // A catalog model mentioned in a delivery date, comparison or description
            // is not a new vehicle. Require the model to begin the cleaned header.
            private static bool IsCatalogVehicleHeader(string line, IReadOnlyCollection<string> models)
            {
                if (string.IsNullOrWhiteSpace(line) ||
                    Regex.IsMatch(line, @"^(?:معاوضه|تعویض|مقایسه|مشابه|در\s+حد|با\s+خودروی|برج|روز|مدل|قیمت|مبلغ|فروش|تحویل|رینگ|قالپاق)(?:\s|$)"))
                    return false;

                return models.Any(model =>
                {
                    var token = CatalogNormalize(model);
                    if (token.Length == 0) return false;
                    // Avoid interpreting numeric catalog names as delivery months.
                    if (Regex.IsMatch(token, @"^\d{1,2}$")) return false;
                    var normalized = CatalogNormalize(line);
                    return normalized.Equals(token, StringComparison.Ordinal) ||
                        normalized.StartsWith(token + " ", StringComparison.Ordinal);
                });
            }

            private static string ExtractYearCatalog(string block, string header)
            {
                var candidates = new HashSet<string>(StringComparer.Ordinal);
                var first = true;
                foreach (var line in block.Split('\n'))
                {
                    var text = Regex.Split(NormalizeText(line), @"تحویل|پلاک|کارکرد|آمپر|امپر|قیمت|بیعانه|اقساط")[0];
                    var pattern = first
                        ? @"(?<![\p{L}\d/.,-])(?:19\d{2}|20\d{2})(?!\d)(?=$|\s|[/.,]|روز|برج|ب\d)"
                        : @"^\s*(?:مدل\s*[:.]?\s*(?:19\d{2}|20\d{2})(?!\d)|(?:19\d{2}|20\d{2})(?=\s*(?:روز|برج|ب\d|/0?[1-9](?!\d)|/1[0-2](?!\d))))";
                    foreach (Match match in Regex.Matches(text, pattern))
                    {
                        var year = Regex.Match(match.Value, @"(?:19|20)\d{2}").Value;
                        if (year.Length > 0) candidates.Add(year);
                    }
                    first = false;
                }
                return candidates.Count == 1 ? candidates.Single() : null;
            }

            private static string ExtractName(string header)
            {
                var name = NormalizeText(header);

                name = FullYearRegex.Replace(name, " ");
                name = ShortYearRegex.Replace(name, " ");
                name = Regex.Replace(name, @"مدل\s*(?:40[0-9]|0[0-9])(?=\D|$)", " ");
                name = ExplicitPriceRegex.Replace(name, " ");
                name = PriceTokenRegex.Replace(name, " ");
                name = NoiseRegex.Replace(name, " ");
                // Remove delivery and presentation details, never the preceding trim.
                // A standalone "405" in "مدل 405 روز" is not the model of a 207.
                name = Regex.Replace(name,
                    @"\b(?:برج\s*\d{1,2}|روز{2,}\S*|قالپاق|با\s+رینگ\s+شرکتی|رینگ\s+شرکتی)\b",
                    " ");
                name = Regex.Replace(name, @"\s+", " ").Trim();
                return name.Trim(' ', '-', '/', ',', '،', ':', '؛');
            }

            private static string ExtractPrice(string block)
            {
                var candidates = new HashSet<decimal>();
                foreach (var rawLine in block.Split('\n'))
                {
                    var normalized = PhoneRegex.Replace(NormalizeText(rawLine), " ");
                    if (Regex.IsMatch(normalized, @"پیش\s*پرداخت|بیعانه|اقساط|قسط|ودیعه|کاور|لوازم|بیمه|مالیات"))
                        continue;
                    // Mileage and asking price can coexist on one line. Only accept a
                    // numeric token on such lines when an explicit price label exists.
                    var labelled = Regex.IsMatch(normalized, @"قیمت|مبلغ|نرخ|فروش");
                    if (MileageRegex.IsMatch(normalized) && !labelled) continue;
                    foreach (Match match in ExplicitPriceRegex.Matches(normalized))
                        if (CarPriceReferenceBuilder.TryConvertPrice(match.Value, out var price))
                            candidates.Add(price);
                    foreach (Match match in ColloquialPriceRegex.Matches(normalized))
                    {
                        var amount = match.Groups["amount"].Value.Replace('/', '.').Replace(',', '.');
                        if (decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                            && number >= 1m && number < 100m)
                            candidates.Add(number);
                    }
                    // A bare figure must be on its own line, have a price label,
                    // or follow a price marker. Never treat a date as a price.
                    var priceLine = Regex.Replace(normalized, @"[\p{S}\uFE0F\s]", "");
                    if (Regex.IsMatch(priceLine, @"^\d{1,4}(?:[/.,]\d{3}){2,3}$|^\d{4}[/.,]\d{3}$"))
                    {
                        var digits = Regex.Replace(priceLine, @"\D", "");
                        if (decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var grouped))
                        {
                            var billion = digits.Length >= 9 ? grouped / 1_000_000_000m : grouped / 1_000_000m;
                            if (billion >= 1m && billion < 100m) candidates.Add(billion);
                        }
                        continue;
                    }
                    var bareLine = Regex.IsMatch(priceLine, @"^\d{4,5}$|^\d{1,2}[/.,]\d{3}$|^\d{1,3}(?:,\d{3}){2,3}$");
                    if (!labelled && !bareLine) continue;
                    foreach (Match match in Regex.Matches(normalized,
                        @"(?<![\p{L}\d])(?:\d{1,3}(?:,\d{3}){2,3}|\d{1,2}[/.,]\d{3}|\d{4,5})(?![\d])"))
                    {
                        var token = match.Value;
                        if (Regex.IsMatch(token, @"^(?:13\d{2}|14\d{2}|20\d{2}|40[0-9])$")) continue;
                        if (Regex.IsMatch(token, @"^\d{1,2}[/.,]\d{3}$"))
                        {
                            var decimalText = token.Replace('/', '.').Replace(',', '.');
                            if (decimal.TryParse(decimalText, NumberStyles.Number,
                                CultureInfo.InvariantCulture, out var billion) &&
                                billion >= 1m && billion < 100m)
                                candidates.Add(billion);
                            continue;
                        }
                        if (Regex.IsMatch(token, @"^\d{1,3}(?:,\d{3}){2,3}$"))
                        {
                            var digits = token.Replace(",", "");
                            if (decimal.TryParse(digits, out var toman) && toman >= 1_000_000_000m)
                                candidates.Add(toman / 1_000_000_000m);
                            continue;
                        }
                        if (CarPriceReferenceBuilder.TryConvertPrice(token, out var price))
                            candidates.Add(price);
                    }
                }
                // Preserve the parsed unit for explicit and colloquial prices.
                // A plain numeric price remains in millions (e.g. 4200).
                // The reference builder accepts both formats through TryConvertPrice.
                if (candidates.Count != 1) return null;
                var candidate = candidates.Single();
                foreach (var rawLine in block.Split('\n'))
                {
                    var normalized = PhoneRegex.Replace(NormalizeText(rawLine), " ");
                    if (Regex.IsMatch(normalized, @"پیش\s*پرداخت|بیعانه|اقساط|قسط|ودیعه|کاور|لوازم|بیمه|مالیات"))
                        continue;
                    var explicitMatch = ExplicitPriceRegex.Match(normalized);
                    if (explicitMatch.Success &&
                        CarPriceReferenceBuilder.TryConvertPrice(explicitMatch.Value, out var explicitAmount) &&
                        explicitAmount == candidate)
                        return explicitMatch.Value;
                    var colloquialMatch = ColloquialPriceRegex.Match(normalized);
                    if (colloquialMatch.Success)
                    {
                        var amount = colloquialMatch.Groups["amount"].Value.Replace('/', '.').Replace(',', '.');
                        if (decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                            && number == candidate)
                            return number.ToString(CultureInfo.InvariantCulture) + " میلیارد";
                    }
                }
                return (candidate * 1000m).ToString("0", CultureInfo.InvariantCulture);
            }

            private static string CleanLine(string value)
            {
                var line = NormalizeText(value);
                line = Regex.Replace(line, @"^[\s\p{P}\p{S}]+", string.Empty);
                return Regex.Replace(line, @"\s+", " ").Trim();
            }

            // Archive-only rewriting: preserve calendar and require explicit year evidence.
            internal static string NormalizeArchiveYears(string message, IReadOnlyCollection<string> models)
            {
                var text = NormalizeText(message);
                var lines = text.Replace("\r", "").Split('\n');
                var active = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var header = IsCatalogVehicleHeader(CleanLine(line), models);
                    if (header) active = true;
                    if (Regex.IsMatch(line, @"خریدار|معاوضه|تعویض|مقایسه")) active = false;
                    lines[i] = RewriteYears(line, models, header, active);
                }
                return string.Join("\n", lines);
            }

            private static string RewriteYears(string line, IReadOnlyCollection<string> models, bool header, bool active)
            {
                return Regex.Replace(line, @"(?<![\p{L}\d/.,-])(?:13\d{2}|14\d{2}|20\d{2}|[34]\d{2}|0\d)(?!\d)(?=$|[ \t\p{P}\p{S}]|روز|برج|ب[ \t]*\d)", match =>
                {
                    var before = line[..match.Index];
                    var after = line[(match.Index + match.Length)..];
                    var prefix = CatalogNormalize(before + match.Value);
                    if (models.Any(model => {
                        var name = CatalogNormalize(model);
                        return name == prefix || name.StartsWith(prefix + " ", StringComparison.Ordinal);
                    })) return match.Value;
                    if (Regex.IsMatch(before, @"(?:تحویل|پلاک|ثبت[ \t]*نام|کارکرد|آمپر|امپر|قیمت|مبلغ|بیعانه|اقساط|تیپ|تا|یا)[ \t.:：]*$") ||
                        Regex.IsMatch(before, @"\d[ \t]*[-–—/][ \t]*$") ||
                        Regex.IsMatch(after, @"^[ \t]*(?:تا\b|یا\b|[-–—][ \t]*\d|[/.,]\d{3,}|کیلومتر|آمپر|امپر|میلیون|میلیارد|تومان|ریال|هزار|دانه|عدد)") ||
                        Regex.IsMatch(before, @"تحویل|ثبت[ \t]*نام|کارکرد|قیمت|بیعانه|اقساط"))
                        return match.Value;
                    var labelled = Regex.IsMatch(before, @"(?:مدل|سال[ \t]+ساخت|سال[ \t]+تولید|تولید)[ \t.:：]*$");
                    var qualifier = Regex.IsMatch(after, @"^[ \t.]*(?:روز|برج|ب[ \t]*\d|/0?[1-9](?!\d)|/1[0-2](?!\d))");
                    var standalone = string.IsNullOrWhiteSpace(before) && qualifier;
                    var plausible = match.Value.Length == 4 || Regex.IsMatch(match.Value, @"^(?:39\d|40\d)$");
                    if (!labelled && !(plausible && (header || (active && standalone)))) return match.Value;
                    if (match.Value.Length == 4 && !match.Value.StartsWith("13") && !match.Value.StartsWith("14")) return match.Value;
                    if (!labelled && !qualifier && !string.IsNullOrWhiteSpace(after) &&
                        !Regex.IsMatch(after, @"^[ \t]+(?:سفید|مشکی|خاکستری|برقی|موجود|تحویل|کارشناسی|تیپ|داخل|پلاک|گارانتی|اس\b|s\b)")) return match.Value;
                    var raw = match.Value;
                    if (raw.Length == 3 && raw.StartsWith("39")) raw = "1" + raw;
                    return NormalizeManufacturingYear(raw) ?? match.Value;
                });
            }

            internal static string NormalizeText(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return string.Empty;

                var builder = new StringBuilder(value.Length);

                foreach (var c in value)
                {
                    if (c >= '۰' && c <= '۹')
                        builder.Append((char)('0' + c - '۰'));
                    else if (c >= '٠' && c <= '٩')
                        builder.Append((char)('0' + c - '٠'));
                    else
                    {
                        builder.Append(c switch
                        {
                            'ي' => 'ی',
                            'ك' => 'ک',
                            '\u200c' => ' ',
                            _ => c
                        });
                    }
                }

                return builder.ToString().ToLowerInvariant();
            }
        
    }
}
