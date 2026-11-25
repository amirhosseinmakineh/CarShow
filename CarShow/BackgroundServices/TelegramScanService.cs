using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
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

using static CarShow.BackgroundServices.ParserService;

namespace CarShow.BackgroundServices
{
    public sealed record TelegramScanOutcome(int StatusCode, object? Value)
    {
        public int TotalChannels { get; init; }
        public int FailedChannels { get; init; }
        public int MessagesProcessed { get; init; }
        public int ParsedCars { get; init; }
        public int ReferenceCount { get; init; }
        public int SavedCars { get; init; }
        public Dictionary<string, string> ChannelErrors { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

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

        // Load a committed archive snapshot once. A refresh uses a single SQL transaction,
        // so this one SELECT cannot observe its intermediate delete/insert steps.
        private async Task<Dictionary<string, List<ParserService.ArchivePayload>>> ReadArchivedMessagesAsync(CancellationToken ct)
        {
            var result = new Dictionary<string, List<ParserService.ArchivePayload>>(StringComparer.OrdinalIgnoreCase);
            var connectionString = _db.Database.GetConnectionString()
                ?? throw new InvalidOperationException("CarShow database connection string is missing.");
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 120;
            command.CommandText = """
                WITH Deduplicated AS (
                    SELECT ChannelUsername, TelegramMessageId, Metadata,
                           ROW_NUMBER() OVER (
                               PARTITION BY ChannelUsername, TelegramMessageId
                               ORDER BY CollectedAt DESC, Id DESC
                           ) AS rn
                    FROM dbo.TelegramRawArchiveSnapshots
                    WHERE Metadata IS NOT NULL AND ISJSON(Metadata) = 1
                )
                SELECT ChannelUsername, Metadata
                FROM Deduplicated
                WHERE rn = 1
                ORDER BY ChannelUsername, TelegramMessageId
                """;
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess, ct);
            while (await reader.ReadAsync(ct))
            {
                var channel = reader.GetString(0);
                var message = JsonSerializer.Deserialize<ParserService.ArchivePayload>(reader.GetString(1));
                if (message?.Version != 2 || message.Vehicles == null) continue;
                if (!result.TryGetValue(channel, out var posts))
                    result[channel] = posts = new List<ParserService.ArchivePayload>();
                posts.Add(message);
            }
            return result;
        }

        private static readonly SemaphoreSlim FinalScanGate = new(1, 1);

        private sealed record ArchiveGroup(string Name, string Year, List<decimal> CandidatePricesBillion,
            CarPriceReference? Reference);

        private sealed record ArchiveComparison(int ParsedCars, int EligibleAdvertisements,
            Dictionary<string, int> RejectedByReason, List<ArchiveGroup> Groups);

        private static ArchiveComparison CompareArchive(
            Dictionary<string, List<ParserService.ArchivePayload>> archive, CancellationToken ct)
        {
            var eligible = new List<ParsedCar>();
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var parsed = 0;
            var seen = new HashSet<(string Text, string Model, string? Trim, string? Year, string? Price)>();
            foreach (var message in archive.Values.SelectMany(x => x))
            foreach (var vehicle in message.Vehicles)
            {
                ct.ThrowIfCancellationRequested();
                parsed++;
                if (!seen.Add((ParserService.CatalogNormalize(message.OriginalText), vehicle.Model, vehicle.Trim, vehicle.Year, vehicle.Price)))
                {
                    reasons["DuplicateAdvertisement"] = reasons.GetValueOrDefault("DuplicateAdvertisement") + 1;
                    continue;
                }
                var model = ParserService.CatalogNormalize(vehicle.Model);
                var trim = ParserService.CatalogNormalize(vehicle.Trim);
                // Legacy archive rows store the complete identity in Model.
                // Split only explicit trim markers; never guess an unspecified trim.
                if (trim.Length == 0)
                {
                    var marker = Regex.Match(model, @"(?<!\S)(?:اتوماتیک|اتومات|دنده ای|دنده|پانوراما|پانا|سقف|تیپ|توربو|اپشنال|آپشنال|پلاس|v[1-4]|gl|g|cv t|cvt|mt|at)(?!\S)");
                    if (marker.Success && marker.Index > 0)
                    {
                        trim = model[marker.Index..];
                        model = model[..marker.Index].Trim();
                    }
                }
                var name = model + " " + trim;
                var year = NormalizeManufacturingYear(vehicle.Year);
                var reason = !vehicle.IsSale ? "NonSale" :
                    string.IsNullOrWhiteSpace(vehicle.Model) ? "EmptyVehicleName" :
                    model.Length == 0 || trim.Length == 0 ? "MissingOrInvalidTrim" :
                    year == null ? "MissingOrInvalidYear" :
                    !CarPriceReferenceBuilder.TryConvertPrice(vehicle.Price, out _) ? "MissingOrInvalidPrice" : null;
                if (reason != null)
                {
                    reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                    continue;
                }
                eligible.Add(new ParsedCar { Name = name, Year = year!, Price = vehicle.Price! });
            }
            var groups = new List<ArchiveGroup>();
            foreach (var group in eligible.GroupBy(x => (x.Name, x.Year))
                .OrderBy(x => x.Key.Name, StringComparer.Ordinal).ThenBy(x => x.Key.Year, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var prices = group.Select(x =>
                {
                    CarPriceReferenceBuilder.TryConvertPrice(x.Price, out var price);
                    return price;
                }).OrderBy(x => x).ToList();
                var reference = prices.Count >= 2 ? CarPriceReferenceBuilder.BuildExact(group).SingleOrDefault() : null;
                groups.Add(new ArchiveGroup(group.Key.Name, group.Key.Year, prices, reference));
            }
            return new ArchiveComparison(parsed, eligible.Count, reasons, groups);
        }

        private static object ComparisonReport(Dictionary<string, List<ParserService.ArchivePayload>> archive,
            ArchiveComparison comparison, int deleted = 0, int inserted = 0)
        {
            var approved = comparison.Groups.Where(x => x.Reference != null).ToList();
            return new
            {
                TotalChannels = archive.Count,
                MessagesProcessed = archive.Values.Sum(x => x.Count),
                comparison.ParsedCars,
                comparison.EligibleAdvertisements,
                EligibleGroups = comparison.Groups.Count,
                SingleAdvertisementGroups = comparison.Groups.Count(x => x.CandidatePricesBillion.Count == 1),
                MultipleAdvertisementGroups = comparison.Groups.Count(x => x.CandidatePricesBillion.Count > 1),
                ReferenceCount = approved.Count,
                RejectedGroupCount = comparison.Groups.Count - approved.Count,
                comparison.RejectedByReason,
                DeletedCars = deleted,
                SavedCars = inserted,
                Groups = comparison.Groups.Select(x => new
                {
                    x.Name, x.Year, x.CandidatePricesBillion,
                    AdvertisementCount = x.CandidatePricesBillion.Count,
                    FinalReferencePrice = x.Reference?.ReferencePriceBillion,
                    Reason = x.CandidatePricesBillion.Count < 2 ? "InsufficientAdvertisements" : x.Reference == null ? "PriceDispersionOrInsufficientConsensus" : "ReferenceGroupApproved"
                }),
                References = approved.Select(x => x.Reference)
            };
        }

        public async Task<TelegramScanOutcome> ScanAsync(
            CancellationToken cancellationToken, Action<int, int>? progress = null)
        {
            await FinalScanGate.WaitAsync(cancellationToken);
            try
            {
                var archive = await ReadArchivedMessagesAsync(cancellationToken);
                progress?.Invoke(archive.Count, archive.Count);
                var comparison = CompareArchive(archive, cancellationToken);
                var references = comparison.Groups.Where(x => x.Reference != null)
                    .Select(x => x.Reference!).ToList();
                var counts = references.Count == 0 ? (Deleted: 0, Inserted: 0) :
                    await SaveReferenceCarsAsync(references, cancellationToken);
                return new TelegramScanOutcome(200,
                    ComparisonReport(archive, comparison, counts.Deleted, counts.Inserted))
                {
                    TotalChannels = archive.Count,
                    MessagesProcessed = archive.Values.Sum(x => x.Count),
                    ParsedCars = comparison.ParsedCars,
                    ReferenceCount = references.Count,
                    SavedCars = counts.Inserted
                };
            }
            finally { FinalScanGate.Release(); }
        }

        public async Task<TelegramScanOutcome> DiagnoseRejectionsAsync(CancellationToken cancellationToken)
        {
            var archive = await ReadArchivedMessagesAsync(cancellationToken);
            var comparison = CompareArchive(archive, cancellationToken);
            return new TelegramScanOutcome(200, ComparisonReport(archive, comparison))
            {
                TotalChannels = archive.Count,
                MessagesProcessed = archive.Values.Sum(x => x.Count),
                ParsedCars = comparison.ParsedCars,
                ReferenceCount = comparison.Groups.Count(x => x.Reference != null)
            };
        }

        public static long? MatchCatalogCar(string advertisement, IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
        {
            // Legacy catalog compatibility only; never used by archive comparison.
            static string CanonicalTrim(string value) => Regex.Replace(
                ParserService.CatalogNormalize(value), @"(?<!\p{L})اتومات(?!\p{L})", "اتوماتیک");
            return ParserService.MatchCatalogCar(CanonicalTrim(advertisement),
                catalog.Select(x => (x.Id, x.Model, Tip: CanonicalTrim(x.Tip))).ToList());
        }

        // The database car is the unit of work. Never choose a CarId by searching
        // the entire catalog for each advertisement.
        // Match by database car, but retain only advertisements with one unambiguous owner.
        // The same matcher is used by the production scan and read-only diagnostics.
        private static Dictionary<int, long> MatchAdvertisementOwners(IReadOnlyList<ParsedCar> advertisements, IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
            => ParserService.MatchAdvertisementOwners(advertisements, catalog);

        private static List<ParsedCar> MatchAdvertisementsForCatalogCars(
            IReadOnlyCollection<ParsedCar> advertisements,
            IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
        {
            var ads = advertisements.ToList();
            return MatchAdvertisementOwners(ads, catalog)
                .Select(pair => new ParsedCar
                {
                    Name = "catalog:" + pair.Value,
                    Year = NormalizeManufacturingYear(ads[pair.Key].Year),
                    Price = ads[pair.Key].Price
                }).ToList();
        }

        private async Task<(int Deleted, int Inserted)> SaveReferenceCarsAsync(
            List<CarPriceReference> references, CancellationToken ct)
        {
            if (references.Count == 0) return (0, 0);
            var now = DateTime.UtcNow;
            var cars = references.Select(reference => new Car
            {
                Name = reference.Name,
                Slug = "archive-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { reference.Name, reference.Year })))).ToLowerInvariant(),
                MarketPrice = reference.ReferencePriceBillion * 1_000_000_000m,
                LastUpdated = now,
                IsReferenceApproved = true,
                IsDelete = false,
                Description = JsonSerializer.Serialize(new
                {
                    Type = "TelegramPriceReference",
                    reference.Year,
                    Years = new Dictionary<string, CarPriceReference> { [reference.Year] = reference },
                    CurrentYears = new Dictionary<string, CarPriceReference> { [reference.Year] = reference },
                    reference.AdvertisementCount,
                    reference.ReferencePriceBillion,
                    ComparisonApproved = true,
                    PriceStatus = "Approved",
                    ComparedAtUtc = now
                })
            }).ToList();

            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await _db.Database.ExecuteSqlRawAsync(
                "DECLARE @result int; EXEC @result = sp_getapplock @Resource = 'CarShow.ArchiveReferenceRefresh', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; IF @result < 0 THROW 51000, 'Another reference refresh is running.', 1;", ct);
            var existing = await _db.Cars.ToListAsync(ct);
            var historyCarIds = (await _db.CarPriceHistories.Select(x => x.CarId)
                .Distinct().ToListAsync(ct)).ToHashSet();

            static string? ExistingYear(Car car)
            {
                try
                {
                    using var document = JsonDocument.Parse(car.Description);
                    return document.RootElement.TryGetProperty("Year", out var year)
                        && year.ValueKind == JsonValueKind.String
                        ? NormalizeManufacturingYear(year.GetString()) : null;
                }
                catch (JsonException) { return null; }
            }

            var retainedIds = new HashSet<long>();
            var newCars = new List<Car>();
            foreach (var car in cars)
            {
                var year = ExistingYear(car);
                var matches = existing.Where(x => !retainedIds.Contains(x.Id) &&
                    (x.Slug == car.Slug || (x.Name == car.Name && ExistingYear(x) == year))).ToList();
                var previous = matches.Count == 1 ? matches[0] : null;
                if (previous != null)
                    car.ImageName = previous.ImageName;
                if (previous != null && historyCarIds.Contains(previous.Id))
                {
                    // Preserve the identity of cars referenced by price history.
                    previous.Name = car.Name;
                    previous.Slug = car.Slug;
                    previous.Description = car.Description;
                    previous.MarketPrice = car.MarketPrice;
                    previous.LastUpdated = car.LastUpdated;
                    previous.IsReferenceApproved = true;
                    previous.IsDelete = false;
                    retainedIds.Add(previous.Id);
                }
                else newCars.Add(car);
            }

            var removable = existing.Where(x => !retainedIds.Contains(x.Id) &&
                !historyCarIds.Contains(x.Id)).ToList();
            foreach (var old in existing.Where(x => !retainedIds.Contains(x.Id) &&
                historyCarIds.Contains(x.Id)))
            {
                old.IsReferenceApproved = false;
                old.IsDelete = true;
            }
            _db.Cars.RemoveRange(removable);
            await _db.SaveChangesAsync(ct);
            _db.Cars.AddRange(newCars);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (removable.Count, cars.Count);
        }

        // Troubleshooting only: GET /api/telegram-cars/diagnostics
        // This does not change the production GET response shape.
        public Task<TelegramScanOutcome> GetDiagnostics(CancellationToken cancellationToken)
            => DiagnoseRejectionsAsync(cancellationToken);


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
        // Reads only public posts published during the last 24 hours (UTC).
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
            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-24);
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

                for (var page = 0; page < maxPages; page++)
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
                            var olderPosts = 0;

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

                                // Telegram public preview embeds the original post timestamp in <time datetime="...">.
                                // Never include undated posts: otherwise older ads could contaminate the reference price.
                                var timeNode = node.SelectSingleNode(".//time[@datetime]");
                                var timestampText = timeNode?.GetAttributeValue("datetime", "");
                                if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal, out var postedAt))
                                    continue;
                                if (postedAt.ToUniversalTime() < cutoffUtc)
                                {
                                    olderPosts++;
                                    continue;
                                }
                                if (postedAt.ToUniversalTime() > DateTimeOffset.UtcNow.AddMinutes(5))
                                    continue;

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

                            // Pages are ordered newest first; once older posts appear, stop paging.
                            if (olderPosts > 0 || addedCount == 0 || !oldestMessageId.HasValue)
                                break;

                            beforeMessageId = oldestMessageId.Value;

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

                result[username] = messages
                    .OrderByDescending(x => x.Key)
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

        // =========================================================
        // PRICE CONVERSION AND REFERENCE CALCULATION
        // =========================================================

        internal static class CarPriceReferenceBuilder
        {
            private static readonly Regex ValidYearRegex = new Regex(
                @"^(?:13|14|20)\d{2}$",
                RegexOptions.Compiled);

            private static readonly Regex ExplicitAmountRegex = new Regex(
                @"^(?<amount>\d+(?:[.,/]\d{1,3})?)\s*(?<unit>میلیارد|میلیون)$",
                RegexOptions.Compiled);

            public static List<CarPriceReference> Build(IEnumerable<ParsedCar> cars) => BuildCore(cars, false);

            public static List<CarPriceReference> BuildExact(IEnumerable<ParsedCar> cars) => BuildCore(cars, true);

            private static List<CarPriceReference> BuildCore(
                IEnumerable<ParsedCar> cars, bool exactIdentity)
            {
                if (cars == null)
                    return new List<CarPriceReference>();

                var validCars = cars
                    .Where(car => car != null)
                    .Select(car =>
                    {
                        var name = exactIdentity ? car.Name : NormalizeCarName(car.Name);
                        var year = NormalizeYear(car.Year);

                        decimal priceBillion;
                        var hasPrice = TryConvertPrice(
                            car.Price,
                            out priceBillion);

                        return new
                        {
                            Name = name,
                            ModelKey = exactIdentity ? name : GetModelKey(name),
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
                    .Select(group =>
                    {
                        // Keep a single valid ad, but do not manufacture a price from
                        // two mutually inconsistent ads.
                        var ordered = group.Select(x => x.PriceBillion).OrderBy(x => x).ToList();
                        var mid = ordered.Count / 2;
                        var median = ordered.Count % 2 == 1
                            ? ordered[mid] : (ordered[mid - 1] + ordered[mid]) / 2m;
                        if (ordered.Count == 2 &&
                            (ordered[1] - ordered[0]) / median > 0.15m)
                            return null;

                        var consistent = ordered.Count == 1 ? ordered : ordered.Where(x =>
                            median > 0 && Math.Abs(x - median) <= median * 0.15m).ToList();
                        // With three or more ads, require a strict majority after filtering.
                        if (ordered.Count > 1 &&
                            (consistent.Count == 0 || consistent.Count * 2 <= ordered.Count))
                            return null;

                        // A broad spread without an identifiable outlier is not a
                        // reliable consensus; report it for review rather than averaging.
                        if (consistent.Count >= 3 &&
                            (consistent.Last() - consistent.First()) / median > 0.12m)
                            return null;

                        // Convert every valid price to toman before averaging.
                        var pricesToman = consistent
                            .Select(price => price * 1_000_000_000m)
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
                    .Where(item => item != null)
                    .Select(item => item!)
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
                if (Regex.IsMatch(value, @"^catalog\s+\d+$", RegexOptions.CultureInvariant))
                    return "catalog:" + value.Substring(8).Trim();

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
                return NormalizeManufacturingYear(value);
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

                // Reject malformed or ambiguous numeric representations instead of
                // silently concatenating punctuation and changing the magnitude.
                if (!Regex.IsMatch(value, @"^\d{4,8}$"))
                    return false;

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
