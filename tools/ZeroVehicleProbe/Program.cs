using System.Text.Json;
using CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.Telegram;
using CarShow.Infrastracture.ZeroVehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Explicit local SQLite probe. Never reads or connects to the production connection string.
var configPath = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--") && x.EndsWith(".json")) ?? "CarShow/appsettings.json");
var liveE2e = args.Contains("--live-e2e");
var channels = liveE2e ? new[] { "@outlier_channel", "@lower_channel", "@upper_channel" } : args.Where(x => x.StartsWith('@')).ToArray();
if (channels.Length == 0 && !args.Contains("--market-only")) throw new ArgumentException("Pass at least one public @channel.");
IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(configPath).Build();
if (liveE2e) configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    """{"ZeroVehicles":{"Catalog":[{"Name":"آیتو M5 مدل 2025","Aliases":[],"Trims":[]}]}}"""))).Build();
if (args.Contains("--market-only"))
{
    var liveServices = new ServiceCollection();
    liveServices.AddHttpClient("CarIr", client => { client.Timeout = TimeSpan.FromSeconds(20); client.DefaultRequestHeaders.UserAgent.ParseAdd("CarShowMarketPrice/1.0"); });
    await using var liveProvider = liveServices.BuildServiceProvider();
    var liveMarket = new CarIrMarketPriceCrawler(liveProvider.GetRequiredService<IHttpClientFactory>(), configuration);
    var evidence = new List<object>();
    var queries = args.Where(x => x.StartsWith("--name=")).Select(x => x[7..]).ToArray();
    foreach (var name in queries.Length > 0 ? queries : ZeroVehicleOptions.Read(configuration).Catalog.Select(x => x.Name).Distinct().Take(20))
        evidence.Add(new { VehicleName = name, NameSource = "ExplicitProbeOrCatalog_NotLiveTelegram", Result = await liveMarket.LookupAsync(name, default) });
    var payload = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
    var destination = args.FirstOrDefault(x => x.StartsWith("--report="))?[9..];
    if (destination is not null) await File.WriteAllTextAsync(destination, payload);
    Console.WriteLine(payload); return;
}
var databasePath = Path.Combine(Path.GetTempPath(), $"carshow-zero-probe-{Guid.NewGuid():N}.db");
await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite($"Data Source={databasePath}").Options);
await db.Database.EnsureCreatedAsync();
var services = new ServiceCollection(); services.AddHttpClient("TelegramWebPreview", client => { client.Timeout = TimeSpan.FromSeconds(30); client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CarShowZeroProbe/1.0"); });
services.AddHttpClient("CarIr", client => { client.Timeout = TimeSpan.FromSeconds(20); client.DefaultRequestHeaders.UserAgent.ParseAdd("CarShowMarketPrice/1.0"); });
await using var provider = services.BuildServiceProvider();
ITelegramCollector collector = liveE2e ? new ControlledCaptions() : new TelegramCollector(configuration, provider.GetRequiredService<IHttpClientFactory>(), NullLogger<TelegramCollector>.Instance);
var market = new CarIrMarketPriceCrawler(provider.GetRequiredService<IHttpClientFactory>(), configuration);
var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, configuration, market);
var allRuns = new List<ZeroImportDto>();
foreach (var channel in channels)
{
    var run = await service.EnqueueAsync(new ZeroImportRequest(channel, 100), default);
    var claimed = await service.ClaimAsync(DateTime.UtcNow, default);
    if (claimed != run.Id) throw new InvalidOperationException("Unexpected queued run.");
    await service.ProcessAsync(run.Id, DateTime.UtcNow, default);
    var result = (await service.GetRunAsync(run.Id, default))!; allRuns.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
}
Console.WriteLine(JsonSerializer.Serialize(new { DatabasePath = databasePath, AcceptedListings = await db.Set<ZeroVehicleListing>().CountAsync(), ReferenceGroups = await db.Set<ZeroVehiclePriceGroup>().CountAsync(), OldCars = await db.Cars.CountAsync(), OldListings = await db.CarListings.CountAsync() }));

var names = await db.Set<ZeroVehicleListing>().AsNoTracking().Select(x => x.OriginalName).Distinct().ToListAsync();
var references = new List<object>();
foreach (var name in names) references.Add(new { TelegramVehicleName = name, NormalizedVehicleName = CarIrMarketPriceCrawler.Normalize(name), Result = await market.LookupAsync(name, default) });
var summary = new {
    TestedAt = DateTime.UtcNow, TestKind = liveE2e ? "ControlledTelegram_LiveCarIr_LocalSqlite" : "LiveTelegramAndCarIr_LocalSqlite", Runs = allRuns, References = references,
    ListingStatuses = await db.Set<ZeroVehicleListing>().GroupBy(x => x.PricingStatus).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(),
    MarketStatuses = await db.Set<ZeroVehicleListing>().GroupBy(x => x.MarketStatus).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(),
    Groups = await db.Set<ZeroVehiclePriceGroup>().Select(x => new { x.MarketPrice, FinalPrice = x.ReferencePrice, x.SampleCount, x.SourceCount }).ToListAsync(),
    DatabasePath = databasePath, Stored = await db.Set<ZeroVehicleListing>().CountAsync(), OldCars = await db.Cars.CountAsync(), OldListings = await db.CarListings.CountAsync()
};
var report = args.FirstOrDefault(x => x.StartsWith("--report="))?[9..];
if (report is not null) await File.WriteAllTextAsync(report, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(summary));

if (liveE2e && (summary.Stored != 3 || summary.Groups.Count != 1 || summary.Groups[0].MarketPrice != 14_000_000_000m || summary.Groups[0].FinalPrice != 14_000_000_000m || summary.Groups[0].SampleCount != 2 || summary.OldCars != 0 || summary.OldListings != 0))
    throw new InvalidOperationException("Live E2E acceptance failed; inspect evidence (source prices may have changed).");

sealed class ControlledCaptions : ITelegramCollector
{
    public Task<TelegramCollectionResult> CollectChannelAsync(string channel, CancellationToken ct)
    {
        var amount = channel.Contains("outlier") ? "۵۰۰۰" : channel.Contains("lower") ? "۱۳۹۰۰" : "۱۴۱۰۰";
        return Task.FromResult(new TelegramCollectionResult(true, false,
            [new("1", $"آیتو M5 مدل 2025 صفر {amount}م", DateTime.UtcNow, null, null)], null));
    }
}
