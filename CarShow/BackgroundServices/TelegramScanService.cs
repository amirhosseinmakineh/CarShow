using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
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
using static System.Net.WebRequestMethods;

namespace CarShow.BackgroundServices
{
    public sealed record TelegramScanOutcome(int StatusCode, object? Value);

    public sealed class TelegramScanService
    {
        private static readonly string[] Channels =
        {
"autopalermo                                         ",
"davoodkelasii                                       ",
"tajcarsefr                                          ",
"autozamanzade                                       ",
"Miladforoud                                         ",
"autoland41                                          ",
"autoland41                                          ",
"haminkhodroo                                        ",
"adib_khodro_qom                                     ",
"gallery20moradi                                     ",
"car_sales_gandomi                                   ",
"autosolhi                                           ",
"BehzadSaberi_OxinKhodro                             ",
"auto_ap                                             ",
"avatttgalry                                         ",
"TehranKhodro97                                      ",
"Mohamadrezamansoor                                  ",
"payamkhodrooo                                       ",
"payamkhodrooo                                       ",
"AUTO_TEYMOURI                                       ",
"hahabbesharaty                                      ",
"AutoEsmieh                                          ",
"omidabbasicarsale                                   ",
"otoeiliya                                           ",
"autogalerymhmood                                    ",
"kharid0forosh0mashin                                ",
"eshraghicar                                         ",
"irankodroohadiali                                   ",
"NamayeshgahdaranTehran21                            ",
"NamayeshgahdaranTehran21                            ",
"hashemi_car2                                        ",
"bazarkhodroh                                        ",
"modern_khodrooooo                                   ",
"thehhrankhodrow                                     ",
"Autoplack                                           ",
"automarkethamidnasiri                               ",
"goroohmashin                                        ",
"khodro_mashin_tehran_divar_bama                     ",
"otosefr_Farzadakbari                                ",
"RAFIEICAR                                           ",
"hhirsa                                              ",
"wolfkhodro                                          ",
"Sefidgarali                                         ",
"ARADKHODRO96                                        ",
"Payamrahnama                                        ",
"uotobehroz                                          ",
"autoluxy                                            ",
"otoadelkhani                                        ",
"Autogorgangolbini                                   ",
"ehsaaant                                            ",
"AutoRamin9787                                       ",
"automeysamsaghi                                     ",
"auto_dayy                                           ",
"shakiba_iran_16                                     ",
"Hoseinbabaieqom                                     ",
"auto_royal_meshkin                                  ",
"auto_najafiqom                                      ",
"mehrdadloghmanii                                    ",
"alavicarexhibition                                  ",
"mamatir_khodro_hosseini                             ",
"grohcobrotehran                                     ",
"khazarrkhodro                                       ",
"auto_1abdolmaleki1                                  ",
"autopasdaran                                        ",
"hakimghailarrr                                      ",
"auto_yektaa                                         ",
"jamshidsamadiiii                                    ",
"auto_jahan                                          ",
"ilam98khodro                                        ",
"autosardarr                                         ",
"khodro_nasri                                        ",
"+kF0Dpp9cMl45MjNk                                   ",
"autovalayati                                        ",
"outorashidiyan                                      ",
"sahracar                                            ",
"arasmehri                                           ",
"parsiankhodroqolizade                               ",
"kiu2016                                             ",
"arsinkhodro                                         ",
"bakshabadi                                          ",
"automojtaba66                                       ",
"AutoEightAsgarinia                                  ",
"keyvancar                                           ",
"pejman2616                                          ",
"auto_tabrizi                                        ",
"hesamfatemikaraj                                    ",
"autofezolahi                                        ",
"IRANKHODROMONIRIKASHAN                              "
        };

        private static readonly HttpClient DefaultTelegramClient = CreateHttpClient();
        private readonly HttpClient TelegramClient;

        private readonly ILogger<TelegramScanService> _logger;
        private readonly CarShowContext _db;

        public TelegramScanService(ILogger<TelegramScanService> logger, CarShowContext db)
            : this(logger, db, DefaultTelegramClient)
        {
        }

        public TelegramScanService(ILogger<TelegramScanService> logger, CarShowContext db, HttpClient telegramClient)
        {
            _logger = logger;
            _db = db;
            TelegramClient = telegramClient;
        }

        // Invoked by the scheduled worker, not by an HTTP request.
        public async Task<TelegramScanOutcome> ScanAsync(
            CancellationToken cancellationToken, Action<int, int>? progress = null)
        {
            // 1) Read the last 100 public messages from each channel.
            var channelErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var messagesByChannel = await GetLast100MessagesAsync(
                Channels,
                cancellationToken,
                channelErrors, progress);

            // A failed channel must not discard the successfully retrieved channels.
            if (messagesByChannel.Keys.All(channelErrors.ContainsKey))
                return new TelegramScanOutcome(502, new { Message = "Telegram retrieval failed; no cars were saved.", ChannelErrors = channelErrors });

            // 2) Parse each message. A message may contain multiple cars.
            var parsedCars = messagesByChannel
                .Where(channel => !channelErrors.ContainsKey(channel.Key))
                .SelectMany(channel => channel.Value)
                .SelectMany(TelegramCarParser.Parse)
                .ToList();

            // 3) Group same car name + same manufacturing year and calculate reference prices.
            var priceReferences = CarPriceReferenceBuilder.Build(parsedCars);

            // Only reference groups, never raw advertisements, become Cars rows.
            var savedCars = await SaveReferenceCarsAsync(priceReferences, cancellationToken);

            _logger.LogInformation(
                "Telegram scan completed. Channels: {ChannelCount}, Messages: {MessageCount}, Parsed cars: {CarCount}, Reference groups: {ReferenceCount}",
                messagesByChannel.Count,
                messagesByChannel.Values.Sum(x => x.Count),
                parsedCars.Count,
                priceReferences.Count);

            if (priceReferences.Count == 0)
            {
                _logger.LogWarning(
                    "No price references were generated. Check parsed message count, extracted year/price values, and channel HTML availability.");
            }

            // A genuinely empty result must not silently hide the failure stage.
            if (priceReferences.Count == 0)
            {
                var metrics = CarPriceReferenceBuilder.Diagnose(parsedCars);
                return new TelegramScanOutcome(422, new
                {
                    Message = "No reference prices could be calculated from the retrieved posts.",
                    TotalMessages = messagesByChannel.Values.Sum(x => x.Count),
                    SavedCars = savedCars,
                    ParsedCars = parsedCars.Count,
                    ReferenceCount = priceReferences.Count,
                    ValidCars = metrics.ValidCars,
                    GroupsBeforeMinimumCount = metrics.GroupsBeforeMinimumCount,
                    GroupsWithTwoOrMoreAds = metrics.GroupsWithTwoOrMoreAds,
                    ChannelErrors = channelErrors,
                    ChannelCounts = messagesByChannel.ToDictionary(x => x.Key, x => x.Value.Count)
                });
            }

            return new TelegramScanOutcome(channelErrors.Count > 0 ? 207 : 200, new
            {
                Status = channelErrors.Count > 0 ? "PartialSuccess" : "Completed",
                ParsedCars = parsedCars.Count,
                ReferenceCount = priceReferences.Count,
                SavedCars = savedCars,
                ChannelErrors = channelErrors,
                References = priceReferences
            });
        }

        // Cars has no year column; keep final comparison details in Description JSON.
        private async Task<int> SaveReferenceCarsAsync(List<CarPriceReference> priceReferences, CancellationToken cancellationToken)
        {
            if (priceReferences.Count == 0)
                return 0;

            var rows = priceReferences.Select(reference => new Car
            {
                Name = reference.Name,
                Description = JsonSerializer.Serialize(new
                {
                    Type = "TelegramPriceReference",
                    reference.Year,
                    reference.AdvertisementCount,
                    reference.ComparedPricesBillion,
                    reference.MinPriceBillion,
                    reference.MaxPriceBillion,
                    reference.ReferencePriceBillion
                }),
                MarketPrice = reference.ReferencePriceBillion * 1_000_000_000m,
                CompanyId = null,
                CarModelId = null,
                TipId = null,
                LastUpdated = DateTime.UtcNow
            }).ToList();

            _db.Cars.AddRange(rows);
            await _db.SaveChangesAsync(cancellationToken);
            return rows.Count;
        }

        // Troubleshooting only: GET /api/telegram-cars/diagnostics
        // This does not change the production GET response shape.
        public async Task<TelegramScanOutcome> GetDiagnostics(CancellationToken cancellationToken)
        {
            var channelErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var channels = await GetLast100MessagesAsync(
                Channels, cancellationToken, channelErrors);
            var cars = channels
                .Where(channel => !channelErrors.ContainsKey(channel.Key))
                .SelectMany(channel => channel.Value)
                .SelectMany(TelegramCarParser.Parse)
                .ToList();

            var totalMessages = channels.Values.Sum(messages => messages.Count);
            var nonemptyMessages = channels.Values.Sum(messages =>
                messages.Count(message => !string.IsNullOrWhiteSpace(message)));

            var stats = CarPriceReferenceBuilder.Diagnose(cars);
            var references = CarPriceReferenceBuilder.Build(cars);

            if (channels.Keys.All(channelErrors.ContainsKey))
                return new TelegramScanOutcome(502, new { Message = "Telegram retrieval failed.", ChannelErrors = channelErrors });

            return new TelegramScanOutcome(channelErrors.Count > 0 ? 207 : 200, new
            {
                Status = channelErrors.Count > 0 ? "PartialSuccess" : "Completed",
                Channels = channels.Select(channel => new
                {
                    Name = channel.Key,
                    Messages = channel.Value.Count,
                    NonemptyMessages = channel.Value.Count(message =>
                        !string.IsNullOrWhiteSpace(message))
                }).ToList(),
                TotalMessages = totalMessages,
                NonemptyMessages = nonemptyMessages,
                ParsedCars = cars.Count,
                CarsWithPrice = cars.Count(car => !string.IsNullOrWhiteSpace(car.Price)),
                CarsWithYear = cars.Count(car => !string.IsNullOrWhiteSpace(car.Year)),
                ValidCars = stats.ValidCars,
                GroupsBeforeMinimumCount = stats.GroupsBeforeMinimumCount,
                GroupsWithTwoOrMoreAds = stats.GroupsWithTwoOrMoreAds,
                ReferenceCount = references.Count,
                ChannelErrors = channelErrors,
                ValidExamples = cars
                    .Where(car => !string.IsNullOrWhiteSpace(car.Price) &&
                                  !string.IsNullOrWhiteSpace(car.Year))
                    .Take(12)
                    .Select(car => new { car.Name, Year = FormatDiagnosticYear(car.Year), car.Price })
                    .ToList(),
                Examples = cars.Take(12).Select(car => new
                {
                    car.Name,
                    Year = FormatDiagnosticYear(car.Year),
                    car.Price
                }).ToList()
            });
        }

        // Diagnostic entrypoint to verify grouping using already-parsed data.
        // Does not require access to Telegram.
        public TelegramScanOutcome DiagnoseParsedCars(List<ParsedCar> cars)
        {
            if (cars == null)
                return new TelegramScanOutcome(400, "Send a JSON array of { name, price, year }.");

            var metrics = CarPriceReferenceBuilder.Diagnose(cars);
            var references = CarPriceReferenceBuilder.Build(cars);
            return new TelegramScanOutcome(200, new
            {
                InputCars = cars.Count,
                metrics.ValidCars,
                metrics.GroupsBeforeMinimumCount,
                metrics.GroupsWithTwoOrMoreAds,
                ReferenceCount = references.Count,
                References = references.Select(reference => new CarPriceReference
                {
                    Name = reference.Name,
                    Year = FormatDiagnosticYear(reference.Year),
                    AdvertisementCount = reference.AdvertisementCount,
                    ComparedPricesBillion = reference.ComparedPricesBillion,
                    MinPriceBillion = reference.MinPriceBillion,
                    MaxPriceBillion = reference.MaxPriceBillion,
                    ReferencePriceBillion = reference.ReferencePriceBillion,
                    ReferencePriceText = reference.ReferencePriceText
                }).ToList()
            });
        }

        // Manufacturing years have no month/day or time zone. A Gregorian year spans
        // two Persian years; presenting a single Persian year would invent precision.
        private static string? FormatDiagnosticYear(string? year)
        {
            if (year == null || !int.TryParse(year, out var value) || value < 2000 || value > 2099)
                return year;
            var calendar = new PersianCalendar();
            var first = calendar.GetYear(new DateTime(value, 1, 1));
            var last = calendar.GetYear(new DateTime(value, 12, 31));
            return first == last ? first.ToString(CultureInfo.InvariantCulture)
                : first.ToString(CultureInfo.InvariantCulture) + "/" + last.ToString(CultureInfo.InvariantCulture);
        }

        // =========================================================
        // TELEGRAM READER
        // Reads up to 100 latest posts from every public channel.
        // No Telegram login or phone number is used.
        // =========================================================

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/120.0.0.0 Safari/537.36");

            return client;
        }

        private async Task<Dictionary<string, List<string>>> GetLast100MessagesAsync(
            IEnumerable<string> channels,
            CancellationToken cancellationToken,
            Dictionary<string, string> channelErrors = null,
            Action<int, int>? progress = null)
        {
            var result = new System.Collections.Concurrent.ConcurrentDictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var errors = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var usernames = channels.Select(NormalizeChannel)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var completed = 0;
            using var semaphore = new SemaphoreSlim(3, 3);
            await Task.WhenAll(usernames.Select(async username =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var messages = new Dictionary<long, string>();
                long? beforeMessageId = null;

                // Safety limit in case Telegram repeats a page.
                const int maxPages = 20;

                for (var page = 0; page < maxPages && messages.Count < 100; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var url = "https://t.me/s/" + username;

                    if (beforeMessageId.HasValue)
                        url += "?before=" + beforeMessageId.Value;

                    try
                    {
                        using (var response = await TelegramClient.GetAsync(
                            url,
                            cancellationToken))
                        {
                            response.EnsureSuccessStatusCode();

                            var html = await response.Content.ReadAsStringAsync();
                            var document = new HtmlDocument();
                            document.LoadHtml(html);

                            var nodes = document.DocumentNode.SelectNodes("//*[@data-post]");

                            if (nodes == null || nodes.Count == 0)
                            {
                                if (messages.Count == 0 && channelErrors != null)
                                    errors[username] = "Telegram HTML has no data-post elements; channel may be blocked, private, or preview markup changed.";
                                break;
                            }

                            long? oldestMessageId = null;
                            var addedCount = 0;

                            foreach (var node in nodes)
                            {
                                var post = node.GetAttributeValue("data-post", string.Empty);
                                var separatorIndex = post.LastIndexOf('/');

                                if (separatorIndex <= 0)
                                    continue;

                                var postChannel = post.Substring(0, separatorIndex);

                                if (!postChannel.Equals(
                                    username,
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                long messageId;

                                if (!long.TryParse(
                                    post.Substring(separatorIndex + 1),
                                    out messageId) ||
                                    messageId <= 0)
                                {
                                    continue;
                                }

                                if (beforeMessageId.HasValue &&
                                    messageId >= beforeMessageId.Value)
                                {
                                    continue;
                                }

                                if (!oldestMessageId.HasValue ||
                                    messageId < oldestMessageId.Value)
                                {
                                    oldestMessageId = messageId;
                                }

                                if (messages.ContainsKey(messageId))
                                    continue;

                                var textNode = node.SelectSingleNode(
                                    ".//*[contains(concat(' ', normalize-space(@class), ' '), " +
                                    "' tgme_widget_message_text ')]");

                                var messageText = ExtractMessageText(textNode, document);

                                // Media-only posts are counted as posts too.
                                messages.Add(messageId, messageText);
                                addedCount++;
                            }

                            if (addedCount == 0 || !oldestMessageId.HasValue)
                                break;

                            beforeMessageId = oldestMessageId.Value;

                            if (messages.Count < 100)
                                await Task.Delay(300, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "Could not read Telegram channel {Channel}.",
                            username);

                        if (channelErrors != null)
                            errors[username] = exception.GetType().Name + ": " + exception.Message;

                        break;
                    }
                }

                if (messages.Count == 0 && channelErrors != null &&
                    !errors.ContainsKey(username))
                {
                    errors[username] = "No matching public posts were collected from Telegram preview.";
                }

                result[username] = messages
                    .OrderByDescending(x => x.Key)
                    .Take(100)
                    .Select(x => x.Value)
                    .ToList();

                _logger.LogInformation(
                    "Channel @{Channel}: {MessageCount} messages read.",
                    username,
                    result[username].Count);
                }
                finally
                {
                    progress?.Invoke(Interlocked.Increment(ref completed), usernames.Length);
                    semaphore.Release();
                }
            }));
            if (channelErrors != null)
                foreach (var error in errors)
                    channelErrors[error.Key] = error.Value;
            return result.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static string ExtractMessageText(
            HtmlNode textNode,
            HtmlDocument document)
        {
            if (textNode == null)
                return string.Empty;

            foreach (var br in textNode.Descendants("br").ToList())
            {
                if (br.ParentNode != null)
                {
                    br.ParentNode.ReplaceChild(
                        document.CreateTextNode("\n"),
                        br);
                }
            }

            return HtmlEntity.DeEntitize(textNode.InnerText).Trim();
        }

        private static string NormalizeChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return null;

            var value = channel.Trim();

            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                if (uri.Scheme != Uri.UriSchemeHttps ||
                    (uri.Host != "t.me" && uri.Host != "www.t.me"))
                {
                    return null;
                }

                value = uri.AbsolutePath.Trim('/');

                if (value.StartsWith("s/", StringComparison.OrdinalIgnoreCase))
                    value = value.Substring(2);
            }

            value = value.TrimStart('@').Trim('/');

            return Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9_]{4,31}$")
                ? value
                : null;
        }

        // =========================================================
        // MODELS
        // =========================================================

        public sealed class ParsedCar
        {
            public string Name { get; set; }
            public string Price { get; set; }
            public string Year { get; set; }
        }

        public sealed class CarPriceReference
        {
            public string Name { get; set; }
            public string Year { get; set; }
            public int AdvertisementCount { get; set; }
            public List<decimal> ComparedPricesBillion { get; set; }
            public decimal MinPriceBillion { get; set; }
            public decimal MaxPriceBillion { get; set; }
            public decimal ReferencePriceBillion { get; set; }
            public string ReferencePriceText { get; set; }
        }

        // =========================================================
        // TELEGRAM MESSAGE PARSER
        // Splits multi-car messages and extracts name, price and year.
        // =========================================================

        private static class TelegramCarParser
        {
            private static readonly string[] VehiclePrefixes =
            {
                "بی وای دی", "بی ام و", "ام وی ام", "گگ امپو", "گک امپو",
                "فراتلندر", "فیدلیتی", "اکستریم", "هایلندر", "کرولا",
                "چانگان", "سوزوکی", "دیگنیتی", "بیجینگ", "آریزو", "اریزو",
                "آریسان", "رانا", "تیگو", "سهند", "لوکانو", "تارا",
                "کوییک", "دنا", "شاهین", "هایما", "نیسان", "ریرا",
                "النترا", "سوناتا", "ساینا", "سورن", "ایگل", "اطلس",
                "توسان", "مزدا", "شوال", "تیسان", "جک", "بنز",
                "کمری", "رافور", "لاماری", "رسپکت", "پراید", "پارس",
                "پژو", "ام جی", "mg", "bmw", "207",
                "ایدی", "اینووی", "وایلندر", "گک", "فراتلندر"
            };

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

            public static List<ParsedCar> Parse(string message)
            {
                var result = new List<ParsedCar>();

                if (string.IsNullOrWhiteSpace(message))
                    return result;

                var blocks = SplitMessage(message);

                foreach (var block in blocks)
                {
                    var header = GetVehicleHeader(block);

                    if (string.IsNullOrWhiteSpace(header))
                        continue;

                    var name = ExtractName(header);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    result.Add(new ParsedCar
                    {
                        Name = name,
                        Price = ExtractPrice(block),
                        Year = ExtractYear(block)
                    });
                }

                return result;
            }

            private static List<string> SplitMessage(string message)
            {
                var lines = message.Replace("\r", string.Empty).Split('\n');
                var starts = new List<int>();

                for (var i = 0; i < lines.Length; i++)
                {
                    if (IsVehicleLine(lines[i]))
                        starts.Add(i);
                }

                var blocks = new List<string>();

                for (var i = 0; i < starts.Count; i++)
                {
                    var start = starts[i];
                    var end = i + 1 < starts.Count ? starts[i + 1] : lines.Length;

                    blocks.Add(string.Join("\n", lines.Skip(start).Take(end - start)));
                }

                return blocks;
            }

            private static bool IsVehicleLine(string line)
            {
                var normalized = CleanLine(line);

                return VehiclePrefixes.Any(prefix =>
                    normalized.StartsWith(
                        NormalizeText(prefix),
                        StringComparison.OrdinalIgnoreCase));
            }

            private static string GetVehicleHeader(string block)
            {
                return block
                    .Split('\n')
                    .Select(CleanLine)
                    .FirstOrDefault(IsVehicleLine);
            }

            private static string ExtractName(string header)
            {
                var name = NormalizeText(header);

                name = FullYearRegex.Replace(name, " ");
                name = ShortYearRegex.Replace(name, " ");
                name = ExplicitPriceRegex.Replace(name, " ");
                name = PriceTokenRegex.Replace(name, " ");
                name = NoiseRegex.Replace(name, " ");

                name = Regex.Replace(name, @"\s+", " ").Trim();
                return name.Trim(' ', '-', '/', ',', '،', ':', '؛');
            }

            private static string ExtractPrice(string block)
            {
                var explicitPrices = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                var numericPrices = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var line in block.Split('\n'))
                {
                    var normalized = NormalizeText(line);

                    // Removing a phone number is safer than discarding a line
                    // which may also contain an actual asking price.
                    normalized = PhoneRegex.Replace(normalized, " ");

                    if (MileageRegex.IsMatch(normalized))
                        continue;

                    foreach (Match match in ExplicitPriceRegex.Matches(normalized))
                    {
                        explicitPrices.Add(Regex.Replace(match.Value, @"\s+", " ").Trim());
                    }

                    var lineWithoutYears = FullYearRegex.Replace(normalized, " ");
                    lineWithoutYears = ShortYearRegex.Replace(lineWithoutYears, " ");

                    foreach (Match match in PriceTokenRegex.Matches(lineWithoutYears))
                    {
                        var digits = Regex.Replace(match.Value, @"\D", string.Empty);

                        if (digits.Length < 4 || digits.Length > 8)
                            continue;

                        numericPrices.Add(match.Value);
                    }
                }

                // Explicitly stated price units have priority.
                if (explicitPrices.Count == 1)
                    return explicitPrices.First();

                if (explicitPrices.Count > 1)
                    return null;

                // Never guess when an ad contains multiple possible prices.
                return numericPrices.Count == 1
                    ? numericPrices.First()
                    : null;
            }

            private static string ExtractYear(string block)
            {
                var normalized = NormalizeText(block);
                var header = GetVehicleHeader(block) ?? string.Empty;

                var years = FullYearRegex.Matches(normalized)
                    .Cast<Match>()
                    .Select(x => x.Value)
                    .Distinct()
                    .ToList();

                foreach (Match match in ShortYearRegex.Matches(header))
                {
                    var beforeYear = header.Substring(0, match.Index);

                    // Never interpret the 405 in "پژو 405" as the year 1405.
                    if (Regex.IsMatch(beforeYear, @"پژو\s*$"))
                        continue;

                    var fullYear = "14" + match.Groups["year"].Value.Substring(1);

                    if (!years.Contains(fullYear))
                        years.Add(fullYear);
                }

                // Ambiguous year ranges are not silently assigned a single year.
                if (Regex.IsMatch(header,
                    @"(?<!\d)(?:40[0-9]|14[0-9]{2})\s*(?:تا|ت|[-/])\s*(?:40[0-9]|14[0-9]{2})(?!\d)"))
                {
                    return null;
                }

                return years.Count == 1 ? years[0] : null;
            }

            private static string CleanLine(string value)
            {
                var line = NormalizeText(value);
                line = Regex.Replace(line, @"^[\s\p{P}\p{S}]+", string.Empty);
                return Regex.Replace(line, @"\s+", " ").Trim();
            }

            private static string NormalizeText(string value)
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

        // =========================================================
        // PRICE CONVERSION AND REFERENCE CALCULATION
        // =========================================================

        private static class CarPriceReferenceBuilder
        {
            private static readonly Regex ValidYearRegex = new Regex(
                @"^(?:13|14|20)\d{2}$",
                RegexOptions.Compiled);

            private static readonly Regex ExplicitAmountRegex = new Regex(
                @"^(?<amount>\d+(?:[.,/]\d{1,3})?)\s*(?<unit>میلیارد|میلیون)$",
                RegexOptions.Compiled);

            public static List<CarPriceReference> Build(
                IEnumerable<ParsedCar> cars)
            {
                if (cars == null)
                    return new List<CarPriceReference>();

                var validCars = cars
                    .Where(car => car != null)
                    .Select(car =>
                    {
                        var name = NormalizeCarName(car.Name);
                        var year = NormalizeYear(car.Year);

                        decimal priceBillion;
                        var hasPrice = TryConvertPrice(
                            car.Price,
                            out priceBillion);

                        return new
                        {
                            Name = name,
                            ModelKey = GetModelKey(name),
                            Year = year,
                            HasPrice = hasPrice,
                            PriceBillion = priceBillion
                        };
                    })
                    .Where(car =>
                        !string.IsNullOrWhiteSpace(car.ModelKey) &&
                        car.Year != null &&
                        car.HasPrice)
                    .ToList();

                // Only the original conditions remain:
                // same normalized vehicle name/model + same manufacturing year.
                // No trim extraction, trim requirement, or trim comparison.
                return validCars
                    .GroupBy(car => new
                    {
                        ModelKey = car.ModelKey,
                        Year = car.Year
                    })
                    .Where(group => group.Count() >= 2)
                    .Select(group =>
                    {
                        // Convert every valid price to toman before averaging.
                        var pricesToman = group
                            .Select(car => car.PriceBillion * 1_000_000_000m)
                            .OrderBy(price => price)
                            .ToList();

                        var average = Round(pricesToman.Average() / 1_000_000_000m);

                        return new CarPriceReference
                        {
                            Name = group.Key.ModelKey,
                            Year = group.Key.Year,
                            AdvertisementCount = pricesToman.Count,
                            ComparedPricesBillion = pricesToman
                                .Select(price => Round(price / 1_000_000_000m))
                                .ToList(),
                            MinPriceBillion = Round(pricesToman.First() / 1_000_000_000m),
                            MaxPriceBillion = Round(pricesToman.Last() / 1_000_000_000m),
                            ReferencePriceBillion = average,
                            ReferencePriceText = average.ToString(
                                "0.000",
                                CultureInfo.InvariantCulture) + " میلیارد تومان"
                        };
                    })
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.Year)
                    .ToList();
            }

            public static (int ValidCars, int GroupsBeforeMinimumCount, int GroupsWithTwoOrMoreAds)
                Diagnose(IEnumerable<ParsedCar> cars)
            {
                var eligible = cars
                    .Where(car => car != null)
                    .Select(car => new
                    {
                        Model = GetModelKey(NormalizeCarName(car.Name)),
                        Year = NormalizeYear(car.Year),
                        Price = car.Price
                    })
                    .Where(car =>
                        !string.IsNullOrWhiteSpace(car.Model) &&
                        car.Year != null &&
                        HasValidPrice(car.Price))
                    .ToList();

                var groups = eligible
                    .GroupBy(car => new { car.Model, car.Year })
                    .ToList();

                return (
                    eligible.Count,
                    groups.Count,
                    groups.Count(group => group.Count() >= 2));
            }

            private static bool HasValidPrice(string value)
            {
                decimal price;
                return TryConvertPrice(value, out price);
            }

            private static string GetModelKey(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;

                var value = CreateNameKey(name);

                // Match specific model names before their broader families.
                if (Regex.IsMatch(value, @"(?<!\d)(?:پژو\s*)?207(?!\d)"))
                    return "پژو 207";

                if (Regex.IsMatch(value, @"(?<![\p{L}\p{N}])کرولا\s*کراس(?![\p{L}\p{N}])"))
                    return "کرولا کراس";

                if (Regex.IsMatch(value, @"(?<![\p{L}\p{N}])کرولا(?![\p{L}\p{N}])"))
                    return "کرولا";

                if (Regex.IsMatch(value, @"(?<![\p{L}\p{N}])(?:جک\s*)?j7(?![\p{L}\p{N}])",
                    RegexOptions.IgnoreCase))
                    return "جک J7";

                if (Regex.IsMatch(value, @"(?<![\p{L}\p{N}])(?:پژو\s*)?206(?![\p{L}\p{N}])"))
                    return "پژو 206";

                if (Regex.IsMatch(value, @"(?<![\p{L}\p{N}])پژو\s*پارس(?![\p{L}\p{N}])"))
                    return "پژو پارس";

                var knownModels = new[]
                {
                    "بی وای دی", "بی ام و", "ام وی ام",
                    "فیدلیتی", "اکستریم", "هایلندر", "چانگان",
                    "سوزوکی", "دیگنیتی", "بیجینگ", "آریزو",
                    "آریسان", "رانا", "تیگو", "سهند", "لوکانو",
                    "کوییک", "دنا", "شاهین", "هایما", "نیسان",
                    "ریرا", "النترا", "سوناتا", "ساینا", "سورن",
                    "ایگل", "اطلس", "توسان", "مزدا", "شوال",
                    "تیسان", "بنز", "کمری", "رافور", "لاماری",
                    "رسپکت", "پراید", "پارس", "پژو", "ام جی"
                };

                foreach (var model in knownModels)
                {
                    if (Regex.IsMatch(
                        value,
                        @"(?<![\p{L}\p{N}])" + Regex.Escape(CreateNameKey(model)) +
                        @"(?![\p{L}\p{N}])"))
                    {
                        return CreateNameKey(model);
                    }
                }

                // If the model is not in the known list, do not guess.
                // Group only exact normalized names for that year.
                return value;
            }

            private static string NormalizeCarName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;

                var value = NormalizeText(name);
                value = Regex.Replace(value, @"[^\p{L}\p{N}]+", " ");
                return Regex.Replace(value, @"\s+", " ").Trim();
            }

            private static string CreateNameKey(string name)
            {
                return name
                    .Replace("آ", "ا")
                    .Replace("أ", "ا")
                    .Replace("إ", "ا")
                    .ToLowerInvariant();
            }

            private static string NormalizeYear(string year)
            {
                if (string.IsNullOrWhiteSpace(year))
                    return null;

                var value = NormalizeText(year).Trim();
                return ValidYearRegex.IsMatch(value) ? value : null;
            }

            public static bool TryConvertPrice(
                string rawPrice,
                out decimal priceBillion)
            {
                priceBillion = 0;

                if (string.IsNullOrWhiteSpace(rawPrice))
                    return false;

                var value = NormalizeText(rawPrice).Trim();
                var explicitMatch = ExplicitAmountRegex.Match(value);

                if (explicitMatch.Success)
                {
                    var amountText = explicitMatch.Groups["amount"].Value
                        .Replace('/', '.')
                        .Replace(',', '.');

                    decimal amount;

                    if (!decimal.TryParse(
                        amountText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out amount))
                    {
                        return false;
                    }

                    priceBillion = explicitMatch.Groups["unit"].Value == "میلیارد"
                        ? amount
                        : amount / 1000m;

                    return IsValidPrice(priceBillion);
                }

                var digits = new string(value.Where(char.IsDigit).ToArray());

                if (digits.Length < 4 || digits.Length > 8)
                    return false;

                decimal rawValue;

                if (!decimal.TryParse(
                    digits,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out rawValue))
                {
                    return false;
                }

                priceBillion = digits.Length >= 6
                    ? rawValue / 1000000m
                    : rawValue / 1000m;

                return IsValidPrice(priceBillion);
            }

            private static bool IsValidPrice(decimal price)
            {
                return price > 0 && price < 1000;
            }

            private static decimal Round(decimal value)
            {
                return decimal.Round(
                    value,
                    3,
                    MidpointRounding.AwayFromZero);
            }

            private static string NormalizeText(string value)
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
                            'آ' => 'ا',
                            'أ' => 'ا',
                            'إ' => 'ا',
                            '\u200c' => ' ',
                            _ => c
                        });
                    }
                }

                return builder.ToString().ToLowerInvariant();
            }
        }


    }
}
