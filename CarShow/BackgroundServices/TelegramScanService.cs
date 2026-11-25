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
                if (message?.Version != 1 || message.Vehicles == null) continue;
                if (!result.TryGetValue(channel, out var posts))
                    result[channel] = posts = new List<ParserService.ArchivePayload>();
                posts.Add(message);
            }
            return result;
        }

        // Invoked by the scheduled worker, not by an HTTP request.
        public async Task<TelegramScanOutcome> ScanAsync(
            CancellationToken cancellationToken, Action<int, int>? progress = null)
        {
            // The price calculation is offline: it must never fetch Telegram HTTP pages.
            // A single SQL query loads the committed raw archive into memory.
            var channelErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var messagesByChannel = await ReadArchivedMessagesAsync(cancellationToken);
            progress?.Invoke(messagesByChannel.Count, messagesByChannel.Count);
            if (messagesByChannel.Count == 0)
                return new TelegramScanOutcome(422, new { Message = "Telegram archive is empty; no prices were changed." });

            // Read the catalog before parsing; the model vocabulary is database-driven.
            var catalog = await _db.Cars.AsNoTracking()
                .Where(x => x.CarModelId != null && x.TipId != null)
                .Select(x => new { x.Id, Name = x.Name, Company = x.Company != null ? x.Company.Name : string.Empty, Model = x.CarModel!.Name, Tip = x.Tip!.Name })
                .ToListAsync(cancellationToken);
            var extracted = messagesByChannel.Values.SelectMany(x => x).SelectMany(x => x.Vehicles).ToList();
            var parsedCars = extracted.Select(x => new ParsedCar { Name = x.Model, Year = x.Year, Price = x.Price }).ToList();
            var adsByCar = extracted.Where(x => x.IsSale && x.CarId.HasValue)
                .GroupBy(x => x.CarId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => new ParsedCar
                {
                    Name = "catalog:" + g.Key, Year = x.Year, Price = x.Price
                }).ToList());
            var priceReferences = new List<CarPriceReference>();
            var rejectedReferences = new List<object>();
            foreach (var databaseCar in catalog.OrderBy(x => x.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                adsByCar.TryGetValue(databaseCar.Id, out var carAdvertisements);
                var matched = (carAdvertisements ?? new List<ParsedCar>())
                    .Where(ad => NormalizeManufacturingYear(ad.Year) != null &&
                        CarPriceReferenceBuilder.TryConvertPrice(ad.Price, out _))
                    .Select(ad => new ParsedCar
                    {
                        Name = "catalog:" + databaseCar.Id,
                        Year = NormalizeManufacturingYear(ad.Year),
                        Price = ad.Price
                    }).ToList();
                if (matched.Count == 0) continue;
                // Compare only the same catalog car and manufacturing year.
                var built = CarPriceReferenceBuilder.Build(matched);
                priceReferences.AddRange(built);
                foreach (var yearGroup in matched.GroupBy(ad => ad.Year))
                {
                    if (built.Any(reference => reference.Year == yearGroup.Key))
                        continue;
                    var prices = yearGroup.Select(ad =>
                    {
                        decimal amount;
                        return CarPriceReferenceBuilder.TryConvertPrice(ad.Price, out amount)
                            ? (decimal?)amount : null;
                    }).Where(amount => amount.HasValue).Select(amount => amount.Value)
                        .OrderBy(amount => amount).ToList();
                    rejectedReferences.Add(new
                    {
                        CarId = databaseCar.Id,
                        Year = yearGroup.Key,
                        Status = "NeedsReview",
                        Reason = "PriceDispersionOrInsufficientConsensus",
                        CandidatePricesBillion = prices,
                        AdvertisementCount = prices.Count
                    });
                }
            }

            // No successful references means no database mutation: preserve existing prices.
            var savedCars = priceReferences.Count == 0
                ? 0
                : await SaveReferenceCarsAsync(priceReferences, rejectedReferences, cancellationToken);

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
                    RejectedReferences = rejectedReferences,
                    ChannelCounts = messagesByChannel.ToDictionary(x => x.Key, x => x.Value.Count)
                })
                {
                    TotalChannels = messagesByChannel.Count,
                    FailedChannels = channelErrors.Count,
                    MessagesProcessed = messagesByChannel.Values.Sum(x => x.Count),
                    ParsedCars = parsedCars.Count,
                    ReferenceCount = priceReferences.Count,
                    SavedCars = savedCars,
                    ChannelErrors = channelErrors
                };
            }

            return new TelegramScanOutcome(channelErrors.Count > 0 ? 207 : 200, new
            {
                Status = channelErrors.Count > 0 ? "PartialSuccess" : "Completed",
                ParsedCars = parsedCars.Count,
                ReferenceCount = priceReferences.Count,
                SavedCars = savedCars,
                ChannelErrors = channelErrors,
                RejectedReferences = rejectedReferences,
                References = priceReferences.Select(reference =>
                {
                    var carIdText = reference.Name.StartsWith("catalog:", StringComparison.Ordinal)
                        ? reference.Name.Substring("catalog:".Length) : string.Empty;
                    var car = long.TryParse(carIdText, out var carId)
                        ? catalog.FirstOrDefault(item => item.Id == carId) : null;
                    return new
                    {
                        reference.Name,
                        CarName = car?.Name ?? reference.Name,
                        CompanyName = car?.Company ?? string.Empty,
                        ModelName = car?.Model ?? string.Empty,
                        TipName = car?.Tip ?? string.Empty,
                        reference.Year,
                        reference.AdvertisementCount,
                        reference.ReferencePriceBillion,
                        reference.ComparedPricesBillion
                    };
                }).ToList()
            })
            {
                TotalChannels = messagesByChannel.Count,
                FailedChannels = channelErrors.Count,
                MessagesProcessed = messagesByChannel.Values.Sum(x => x.Count),
                ParsedCars = parsedCars.Count,
                ReferenceCount = priceReferences.Count,
                SavedCars = savedCars,
                ChannelErrors = channelErrors
            };
        }

        // Diagnostic-only classification; matching and reference-price rules remain unchanged.
        private static (string Reason, string Detail, string? Model, int CandidateCount)
            ExplainCatalogRejection(string advertisement,
                IReadOnlyCollection<(long Id, string Model, string Tip)> catalog)
        {
            var text = CatalogNormalize(advertisement);
            if (text.Length == 0)
                return ("EmptyVehicleName", "Vehicle name is empty after normalization.", null, 0);

            var models = catalog.Select(x => x.Model).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(model => CatalogContains(text, model)).ToList();
            if (models.Count == 0)
                return ("ModelNotFound", "No exact normalized catalog model phrase was found.", null, 0);

            var specificModels = models.Where(model => !models.Any(other =>
                !string.Equals(model, other, StringComparison.OrdinalIgnoreCase) &&
                CatalogContains(other, model))).ToList();
            if (specificModels.Count != 1)
                return ("AmbiguousModel", "Multiple unrelated catalog models match the advertisement.", null, specificModels.Count);

            var model = specificModels[0];
            var candidates = catalog.Where(item =>
                string.Equals(item.Model, model, StringComparison.OrdinalIgnoreCase) &&
                TrimContains(text, item.Tip)).ToList();
            if (candidates.Count == 0)
                return ("TrimNotFound", "Model matched but no catalog trim phrase matched.", model, 0);

            var specificTrims = candidates.Where(item => !candidates.Any(other =>
                item.Id != other.Id && IsStrictlyMoreSpecificTrim(other.Tip, item.Tip))).ToList();
            return specificTrims.Count == 1
                ? ("UnclassifiedMatchFailure", "Diagnostic classification differs from matcher.", model, specificTrims.Count)
                : ("AmbiguousTrim", "Multiple catalog trims or duplicate catalog entries match.", model, specificTrims.Count);
        }

        // Read-only diagnostic scan: fetches the same Telegram source and catalog as ScanAsync,
        // but never calls SaveReferenceCarsAsync and never modifies database records.
        public async Task<TelegramScanOutcome> DiagnoseRejectionsAsync(CancellationToken cancellationToken)
        {
            var channelErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var messagesByChannel = await GetLast100MessagesAsync(
                Channels, cancellationToken, channelErrors, null);
            var catalog = await _db.Cars.AsNoTracking()
                .Where(x => x.CarModelId != null && x.TipId != null)
                .Select(x => new { x.Id, Model = x.CarModel!.Name, Tip = x.Tip!.Name })
                .ToListAsync(cancellationToken);
            var models = catalog.Select(x => x.Model).Distinct().ToArray();
            var catalogTuples = catalog.Select(x => (Id: (long)x.Id, Model: x.Model, Tip: x.Tip)).ToList();
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var samples = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            var accepted = new List<ParsedCar>();
            var parsedCount = 0;
            var messages = messagesByChannel.Where(x => !channelErrors.ContainsKey(x.Key))
                .SelectMany(x => x.Value).Distinct(StringComparer.Ordinal).ToList();
            var noVehicleMessages = 0;

            void Reject(string reason, ParsedCar car, string detail = "", string? matchedModel = null, int candidateCount = 0)
            {
                reasons[reason] = reasons.TryGetValue(reason, out var count) ? count + 1 : 1;
                if (!samples.TryGetValue(reason, out var examples))
                    samples[reason] = examples = new List<object>();
                if (examples.Count < 5)
                    examples.Add(new { car.Name, Year = NormalizeManufacturingYear(car.Year), car.Price, Reason = reason, Detail = detail, MatchedModel = matchedModel, CandidateCount = candidateCount });
            }

            var parsedAdvertisements = new List<ParsedCar>();
            foreach (var message in messages)
            {
                var cars = ParserService.ParseCatalog(message, models);
                if (cars.Count == 0) noVehicleMessages++;
                parsedAdvertisements.AddRange(cars);
            }
            parsedCount = parsedAdvertisements.Count;
            var owners = MatchAdvertisementOwners(parsedAdvertisements, catalogTuples);
            for (var i = 0; i < parsedAdvertisements.Count; i++)
            {
                var car = parsedAdvertisements[i];
                if (!owners.TryGetValue(i, out var id))
                {
                    var diagnosis = ExplainCatalogRejection(car.Name, catalogTuples);
                    Reject(diagnosis.Reason == "UnclassifiedMatchFailure" ? "AmbiguousCatalogMatch" : diagnosis.Reason,
                        car, diagnosis.Detail, diagnosis.Model, diagnosis.CandidateCount);
                    continue;
                }
                var year = NormalizeManufacturingYear(car.Year);
                if (year == null)
                {
                    Reject("MissingOrInvalidYear", car, "Manufacturing year was not extracted or unsupported.");
                    continue;
                }
                if (!CarPriceReferenceBuilder.TryConvertPrice(car.Price, out _))
                {
                    Reject("MissingOrInvalidPrice", car, "Sale price missing or invalid.");
                    continue;
                }
                accepted.Add(new ParsedCar { Name = "catalog:" + id, Year = year, Price = car.Price });
            }

            var groups = accepted.GroupBy(x => new { x.Name, x.Year }).ToList();
            var references = CarPriceReferenceBuilder.Build(accepted);
            var referenceKeys = references.Select(x => (x.Name, x.Year)).ToHashSet();
            var rejectedGroups = groups.Where(x => !referenceKeys.Contains((x.Key.Name, x.Key.Year)))
                .Select(x => new { x.Key.Name, x.Key.Year, Advertisements = x.Count() })
                .ToList();
            return new TelegramScanOutcome(200, new
            {
                Note = "Read-only live fetch; counts can differ from a previous scan. No database writes.",
                // Investigation mode: return every fetched Telegram message verbatim.
                // No model, trim, year or price filters are applied to this dataset.
                // RawMessage is intentionally not passed through NormalizeText.
                RawTelegramMessages = messagesByChannel
                    .SelectMany(channel => channel.Value.Select((message, index) => new
                    {
                        Channel = channel.Key,
                        Index = index,
                        RawMessage = message
                    })).ToList(),
                // Shadow token observations: no catalog matching, no price acceptance,
                // and no changes to production reference-price calculations.
                ShadowTokenObservations = messagesByChannel
                    .SelectMany(channel => channel.Value.Select((message, index) => new
                    {
                        Channel = channel.Key,
                        Index = index,
                        Tokens = TelegramShadowExtractor.Inspect(message)
                    })).ToList(),
                // Parsed values are observational only; no catalog-match requirement.
                ParsedAdvertisements = parsedAdvertisements.Select(car => new
                {
                    car.Name,
                    car.Year,
                    car.Price
                }).ToList(),
                TotalChannels = messagesByChannel.Count,
                FailedChannels = channelErrors.Count,
                MessagesProcessed = messages.Count,
                MessagesWithoutRecognizedCar = noVehicleMessages,
                ParsedCars = parsedCount,
                CatalogCars = catalog.Count,
                RejectedByReason = reasons,
                RejectedExamples = samples,
                MatchedWithValidYearAndPrice = accepted.Count,
                EligibleGroups = groups.Count,
                ReferenceCount = references.Count,
                SingleAdvertisementReferences = references.Count(x => x.AdvertisementCount == 1),
                RejectedPriceConsensusGroups = rejectedGroups.Count,
                RejectedPriceConsensusExamples = rejectedGroups.Take(10).ToList(),
                ChannelErrors = channelErrors
            })
            {
                TotalChannels = messagesByChannel.Count,
                FailedChannels = channelErrors.Count,
                MessagesProcessed = messages.Count,
                ParsedCars = parsedCount,
                ReferenceCount = references.Count,
                ChannelErrors = channelErrors
            };
        }

        public static long? MatchCatalogCar(string advertisement, IReadOnlyCollection<(long Id, string Model, string Tip)> catalog) => ParserService.MatchCatalogCar(advertisement, catalog);

        // The database car is the unit of work. Never choose a CarId by searching
        // the entire catalog for each advertisement.
        // Match by database car, but retain only advertisements with one unambiguous owner.
        // The same matcher is used by the production scan and read-only diagnostics.
        private static Dictionary<int, long> MatchAdvertisementOwners(
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

        // Only update existing catalog cars after a genuine reference was calculated.
        // Keep all verified years in Description; do not insert a car per advertisement/year.
        private async Task<int> SaveReferenceCarsAsync(List<CarPriceReference> priceReferences, List<object> rejectedReferences, CancellationToken cancellationToken)
        {
            // Reconcile every previously approved Telegram reference, not only cars
            // that happened to have an acceptable price in this scan.
            var currentIds = priceReferences
                .Where(x => x.Name.StartsWith("catalog:", StringComparison.Ordinal))
                .Select(x => long.TryParse(x.Name.Substring(8), out var id) ? id : 0L)
                .Where(x => x > 0).Distinct().ToList();
            var cars = await _db.Cars
                .Where(x => x.CarModelId != null && x.TipId != null
                    && (currentIds.Contains(x.Id) || (x.Description != null
                        && x.Description.Contains("\"Type\":\"TelegramPriceReference\""))))
                .ToListAsync(cancellationToken);
            // Resolve shared model images once per scan; do not alter the price comparison.
            var modelImages = await _db.CarModels.AsNoTracking()
                .Where(x => x.ImageName != null && x.ImageName != "")
                .Select(x => new { x.Id, x.ImageName })
                .ToDictionaryAsync(x => x.Id, x => x.ImageName!, cancellationToken);
            var referencesByCar = priceReferences
                .Where(x => x.Name.StartsWith("catalog:", StringComparison.Ordinal))
                .GroupBy(x => long.TryParse(x.Name.Substring(8), out var id) ? id : 0)
                .ToDictionary(x => x.Key, x => x.ToList());
            var rejectedByCar = rejectedReferences
                .Select(x => JsonSerializer.SerializeToElement(x))
                .Where(x => x.TryGetProperty("CarId", out _))
                .GroupBy(x => x.GetProperty("CarId").GetInt64())
                .ToDictionary(x => x.Key, x => x.ToList());
            var updated = 0;
            foreach (var car in cars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                referencesByCar.TryGetValue(car.Id, out var current);
                rejectedByCar.TryGetValue(car.Id, out var rejected);
                current ??= new List<CarPriceReference>();
                rejected ??= new List<JsonElement>();
                var historical = new Dictionary<string, CarPriceReference>(StringComparer.Ordinal);
                try
                {
                    using var document = JsonDocument.Parse(car.Description!);
                    if (document.RootElement.TryGetProperty("Years", out var years)
                        && years.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var year in years.EnumerateObject())
                        {
                            var old = year.Value.Deserialize<CarPriceReference>();
                            if (old != null) historical[year.Name] = old;
                        }
                    }
                }
                catch (JsonException) { /* Ignore malformed historical JSON. */ }
                foreach (var reference in current)
                    historical[reference.Year] = reference;
                var selected = current
                    .OrderByDescending(x => int.TryParse(x.Year, out var year) ? year : 0)
                    .FirstOrDefault();
                // Historical prices are retained for audit, but never reused as a
                // current approval when the scan produced no valid reference.
                if (selected != null)
                {
                    car.MarketPrice = selected.ReferencePriceBillion * 1_000_000_000m;
                    updated++;
                }
                if (selected != null && string.IsNullOrWhiteSpace(car.ImageName)
                    && car.CarModelId is long modelId
                    && modelImages.TryGetValue(modelId, out var modelImage))
                    car.ImageName = modelImage;
                car.IsReferenceApproved = selected != null;
                car.Description = JsonSerializer.Serialize(new
                {
                    Type = "TelegramPriceReference",
                    Year = selected?.Year,
                    Years = historical,
                    CurrentYears = current.ToDictionary(reference => reference.Year, reference => reference, StringComparer.Ordinal),
                    AdvertisementCount = selected?.AdvertisementCount ?? 0,
                    ReferencePriceBillion = selected?.ReferencePriceBillion ?? 0m,
                    ComparisonApproved = selected != null,
                    PriceStatus = selected == null
                        ? (rejected.Count > 0 ? "NeedsReview" : "Stale")
                        : "Approved",
                    Confidence = selected == null ? "None" :
                        selected.AdvertisementCount == 1 ? "Low" :
                        selected.AdvertisementCount == 2 ? "Medium" :
                        (selected.MaxPriceBillion - selected.MinPriceBillion)
                            / selected.ReferencePriceBillion <= 0.10m ? "High" : "Medium",
                    RejectedReferences = rejected,
                    ComparedAtUtc = DateTime.UtcNow
                });
                car.LastUpdated = DateTime.UtcNow;
            }
            if (cars.Count > 0) await _db.SaveChangesAsync(cancellationToken);
            return updated;
        }

        // Troubleshooting only: GET /api/telegram-cars/diagnostics
        // This does not change the production GET response shape.
        public async Task<TelegramScanOutcome> GetDiagnostics(CancellationToken cancellationToken)
        {
            var channelErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var channels = await GetLast100MessagesAsync(
                Channels, cancellationToken, channelErrors);
            var models = await _db.CarModels.AsNoTracking().Select(x => x.Name).ToListAsync(cancellationToken);
            var cars = channels
                .Where(channel => !channelErrors.ContainsKey(channel.Key))
                .SelectMany(channel => channel.Value)
                .SelectMany(message => ParserService.ParseCatalog(message, models))
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
