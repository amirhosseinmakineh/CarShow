using System.Security.Claims;
using System.Text;
using CarShow.ApplicationService.Services;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Controllers;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarShow.Tests;

public sealed class P0PersistenceTests
{
    private sealed class Queue : ITelegramCollectionQueue {
        public ValueTask EnqueueAsync(long id, CancellationToken ct = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) { await Task.CompletedTask; yield break; }
    }
    private sealed class Collector : ITelegramCollector {
        public string Text = "";
        public Task<TelegramCollectionResult> CollectChannelAsync(string channel, CancellationToken ct)
            => Task.FromResult(new TelegramCollectionResult(true, false, [new("1", Text, DateTime.UtcNow, "https://t.me/" + channel + "/1", null)], null));
    }
    private sealed class Market(bool wrongYear = false) : ICarIrMarketPriceCrawler {
        public Task<MarketPriceResult> LookupAsync(string name, CancellationToken ct) => Task.FromResult(
            new MarketPriceResult("Matched", wrongYear ? "تارا 1402" : name, 1_000_000_000m, DateTime.UtcNow, DateTime.UtcNow, "https://car.ir/fixture"));
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
        """{"ZeroVehicles":{"Catalog":[{"Name":"تارا","Aliases":[],"Trims":[]}]}}"""))).Build();

    [Fact]
    public async Task UsedMeanManualOverrideAuditRecalculationAndRevocationPersistIndependently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        void Add(decimal price, string identifier) {
            var channel = new TelegramChannel { NormalizedIdentifier = identifier, Username = identifier };
            db.Add(channel); db.SaveChanges();
            db.Add(new CarListing { ChannelId = channel.Id, SourceMessageId = "1", SourceChannel = identifier,
                Model = "پژو 207", Manufacturer = "پژو", Trim = "اتومات", IsUsed = true, Year = 1401, YearCalendar = "Jalali", Mileage = 20000,
                Price = price, NormalizedPrice = price, Currency = "IRT", PublishedAt = now, PriceDay = CarListingMatcher.TehranDay(now),
                ComparisonKey = CarListingMatcher.BuildComparisonKey("پژو", "پژو 207", "اتومات", 1401, "Jalali", true, 20000),
                Fingerprint = identifier });
            db.SaveChanges();
        }
        Add(900_000_000m, "dealer_one"); Add(940_000_000m, "dealer_two");
        var store = new TelegramProcessingStore(db); var zeroStore = new ZeroVehicleStore(db);
        var used = new TelegramPricingService(store, new TelegramListingExtractor(), new Queue(), Config(), NullLogger<TelegramPricingService>.Instance);
        var zero = new ZeroVehicleService(zeroStore, new Collector(), Config());
        await used.CalculateAsync(now, default);
        var car = await db.Cars.SingleAsync(); Assert.Equal(920_000_000m, car.MarketPrice);
        var controller = new MarketReviewController(db, store, zeroStore, used, zero) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-42")], "test")) } }
        };
        Assert.IsType<NoContentResult>(await controller.Override("used", car.Id, new(1_000_000_000m, null, "قیمت بررسی شد"), default));
        Add(980_000_000m, "dealer_three"); await used.CalculateAsync(now.AddMinutes(1), default);
        Assert.Equal(940_000_000m, car.CalculatedMarketPrice); Assert.Equal(1_000_000_000m, car.MarketPrice);
        var audit = await db.Set<MarketPriceReview>().SingleAsync(); Assert.Equal("admin-42", audit.Actor);
        Assert.Equal(900_000_000m, (await db.CarListings.OrderBy(x => x.Id).FirstAsync()).Price);
        Assert.IsType<NoContentResult>(await controller.Override("used", car.Id, new(null, null, "بازگشت به قیمت محاسبه‌شده"), default));
        Assert.Equal(940_000_000m, car.MarketPrice); Assert.Null(car.ManualOverridePrice);
        Assert.Equal(2, await db.Set<MarketPriceReview>().CountAsync());
        await used.RequestRecalculationAsync(default);
        db.ChangeTracker.Clear();
        var restarted = new TelegramPricingService(new TelegramProcessingStore(db), new TelegramListingExtractor(), new Queue(), Config(), NullLogger<TelegramPricingService>.Instance);
        Assert.True(await restarted.HasPricingWorkAsync(DateTime.UtcNow, default));
        await restarted.CalculateAsync(DateTime.UtcNow, default);
        var state = await db.TelegramPricingStates.AsNoTracking().SingleAsync();
        Assert.NotNull(state.PricingRequestedAt); Assert.Equal(state.PricingRequestedAt, state.PricingRequestCompletedAt);
    }

    [Fact]
    public async Task ZeroYearsRemainSeparateAndReplayDoesNotAddSamples()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collector = new Collector(); var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), new Market());
        foreach (var (channel, year, price) in new[] { ("dealer_one", 1401, 900), ("dealer_two", 1402, 940) }) {
            collector.Text = $"تارا صفر مدل: {year} قیمت: {price} میلیون تومان";
            var run = await service.EnqueueAsync(new(channel), default); await service.ClaimAsync(DateTime.UtcNow, default);
            await service.ProcessAsync(run.Id, DateTime.UtcNow, default);
        }
        var groups = await db.Set<ZeroVehiclePriceGroup>().Where(x => x.PricingStatus == "Priced").OrderBy(x => x.Year).ToListAsync();
        Assert.Equal(2, groups.Count); Assert.Equal(1401, groups[0].Year); Assert.Equal(1402, groups[1].Year);
        Assert.All(groups, x => { Assert.Equal(1, x.SourceCount); Assert.Equal("Jalali", x.YearCalendar); Assert.NotNull(x.PriceDay); });
        var replay = await service.EnqueueAsync(new("dealer_two"), default); await service.ClaimAsync(DateTime.UtcNow, default);
        await service.ProcessAsync(replay.Id, DateTime.UtcNow, default);
        Assert.Equal(2, await db.Set<ZeroVehicleListing>().CountAsync());
        Assert.Empty(await db.CarListings.ToListAsync()); Assert.Empty(await db.Cars.ToListAsync());
        await service.RequestRecalculationAsync(default); db.ChangeTracker.Clear();
        var restarted = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), new Market());
        Assert.True(await restarted.RequiresRecalculationAsync(DateTime.UtcNow, default));
        await restarted.RecalculateAsync(DateTime.UtcNow, default);
        Assert.False(await restarted.RequiresRecalculationAsync(DateTime.UtcNow, default));
    }

    [Fact]
    public async Task ZeroNeverBorrowsReferenceFromAnotherYearAndKeepsTheAdvertisement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collector = new Collector { Text = "تارا صفر مدل: 1401 قیمت: 900 میلیون تومان" };
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), new Market(true));
        var run = await service.EnqueueAsync(new("dealer_one"), default); await service.ClaimAsync(DateTime.UtcNow, default);
        await service.ProcessAsync(run.Id, DateTime.UtcNow, default);
        var row = await db.Set<ZeroVehicleListing>().SingleAsync();
        Assert.Equal("Ambiguous", row.MarketStatus); Assert.Equal("Unavailable", row.PricingStatus);
        Assert.Equal(collector.Text, row.RawText); Assert.Null(row.FinalPrice);
        Assert.Empty(await db.Set<ZeroVehiclePriceGroup>().ToListAsync());
    }

    [Fact]
    public async Task BothServicesUseSharedRegistryAndIgnoreAnUnregisteredZeroCheckpoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var usedStore = new TelegramProcessingStore(db);
        var management = new TelegramChannelService(new BaseRepository<long, TelegramChannel>(db),
            new BaseRepository<long, TelegramCollectionJob>(db), new Queue(), Config(), usedStore);
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), new Collector(), Config(), new Market(), usedStore, management);
        await service.AddChannelsAsync("HTTP://t.me/Dealer_One/\n@dealer_one\nhttps://t.me/+kF0Dpp9cMl45MjNk", default);
        Assert.Equal(2, await db.TelegramChannels.CountAsync());
        Assert.Equal(2, (await service.ChannelsAsync(default)).Count);
        Assert.Empty(await db.Set<ZeroVehicleChannel>().ToListAsync());
        db.Add(new ZeroVehicleChannel { Identifier = "rogue_channel", Enabled = true }); await db.SaveChangesAsync();
        await service.ScheduleCollectionAsync(DateTime.UtcNow, default);
        Assert.Equal("dealer_one", (await db.Set<ZeroVehicleImportRun>().SingleAsync()).Channel);
        var shared = await db.TelegramChannels.SingleAsync(x => x.NormalizedIdentifier == "dealer_one");
        await service.ChangeChannelAsync(shared.Id, false, default);
        Assert.False(shared.Enabled);
        Assert.Empty(await service.ImportAllAsync(100, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.EnqueueAsync(new("rogue_channel"), default));
        Assert.Equal("AccessRequired", (await db.TelegramChannels.SingleAsync(x => x.NormalizedIdentifier.StartsWith("+"))).Status.ToString());
    }

    [Fact]
    public async Task CatalogRepairUsesStoredPublicationDateAndPreservesOriginalIssueAndRawBody()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collector = new Collector { Text = "جدیدکار صفر قیمت: 900 میلیون تومان" };
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), new Market());
        var run = await service.EnqueueAsync(new("dealer_one"), default); await service.ClaimAsync(DateTime.UtcNow, default);
        await service.ProcessAsync(run.Id, DateTime.UtcNow, default);
        var issue = await db.Set<ZeroVehicleIssue>().SingleAsync(); Assert.NotNull(issue.PublishedAt);
        var model = new ZeroVehicleModel { Name = "جدیدکار", Trims = [new ZeroVehicleTrim { Name = "نام‌محور" }] };
        db.Add(model); (await db.Set<ZeroVehicleSetting>().SingleAsync()).RecalculationRequestedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(); await service.RecalculateAsync(DateTime.UtcNow, default);
        var listing = await db.Set<ZeroVehicleListing>().SingleAsync();
        Assert.Equal(collector.Text, issue.RawText); Assert.Equal(issue.RawText, listing.RawText);
        Assert.Equal(issue.PublishedAt, listing.PublishedAt); Assert.Equal(listing.Id, issue.ResolvedListingId);
        Assert.NotNull(issue.ResolvedAt); Assert.Equal("MODEL_UNKNOWN_OR_AMBIGUOUS", issue.Code);
        Assert.Single(await db.Set<MarketPriceReview>().Where(x => x.Scope == "zero-issue").ToListAsync());
        await service.RecalculateAsync(DateTime.UtcNow, default); Assert.Equal(1, await db.Set<ZeroVehicleListing>().CountAsync());
    }

    [Fact]
    public async Task CompletedJobCountersSnapshotAcceptedRowsAndIndependentPriceGroups()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var now = DateTime.UtcNow;
        var channel = new TelegramChannel { NormalizedIdentifier = "dealer_one", Username = "dealer_one" }; db.Add(channel); await db.SaveChangesAsync();
        var job = new TelegramCollectionJob { ChannelId = channel.Id, Status = TelegramCollectionJobStatus.Succeeded,
            StartedAt = now.AddMinutes(-2), CompletedAt = now, NewRawMessages = 2, ExtractedListings = 2 };
        db.Add(job);
        foreach (var (id, price) in new[] { ("1", 900), ("2", 940) }) db.Add(new TelegramRawMessage { ChannelId = channel.Id,
            ExternalMessageId = id, MessageText = $"پژو 207 اتومات مدل 1401 کارکرد 20000 قیمت {price} میلیون تومان",
            MessageDate = now.AddSeconds(-3 + int.Parse(id)), CollectedAt = now.AddSeconds(-1) });
        await db.SaveChangesAsync();
        var service = new TelegramPricingService(new TelegramProcessingStore(db), new TelegramListingExtractor(), new Queue(), Config(), NullLogger<TelegramPricingService>.Instance);
        await service.CalculateAsync(now, default);
        Assert.Equal(2, job.AcceptedListings); Assert.Equal(0, job.RejectedListings); Assert.Equal(1, job.CalculatedPriceGroups);
        Assert.Equal(now, job.PricingCompletedAt);
        (await db.CarListings.OrderBy(x => x.Id).LastAsync()).AdminDecision = "Rejected"; await db.SaveChangesAsync();
        await service.CalculateAsync(now.AddMinutes(1), default); Assert.Equal(2, job.AcceptedListings);
    }

    [Fact]
    public async Task LegacyPublishedUsedRowsAreSplitByExplicitYearsWithoutErasingOldHistoryOrRawText()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var now = DateTime.UtcNow;
        var key = CarListingMatcher.BuildComparisonKey("پژو", "پژو 207", "اتومات", null, null, true, 20000);
        var oldCar = new Car { Name = "legacy", CarModel = new() { Name = "پژو 207" }, Company = new() { Name = "پژو" },
            Tip = new() { Name = "اتومات" }, IsUsed = true, TelegramIdentityKey = key, MarketPrice = 950_000_000m,
            CalculatedMarketPrice = 950_000_000m, MarketPriceDay = CarListingMatcher.TehranDay(now), PriceSampleCount = 2 };
        db.Add(oldCar); await db.SaveChangesAsync();
        db.Add(new CarPriceHistory { CarId = oldCar.Id, PriceDay = oldCar.MarketPriceDay, MarketPrice = oldCar.MarketPrice });
        foreach (var (channelName, year, price) in new[] { ("dealer_one", 1401, 900), ("dealer_two", 1402, 1000) }) {
            var channel = new TelegramChannel { NormalizedIdentifier = channelName, Username = channelName }; db.Add(channel); await db.SaveChangesAsync();
            var text = $"207 اتومات {year} کارکرد 20000 قیمت {price} میلیون تومان";
            var raw = new TelegramRawMessage { ChannelId = channel.Id, ExternalMessageId = "1", MessageText = text, MessageDate = now,
                ExtractionVersion = 5, ProcessingStatus = "Stored" }; db.Add(raw); await db.SaveChangesAsync();
            db.Add(new CarListing { ChannelId = channel.Id, RawMessageId = raw.Id, SourceMessageId = "1", Model = "پژو 207", Trim = "اتومات",
                IsUsed = true, Mileage = 20000, CarId = oldCar.Id, ComparisonKey = key, RawText = text, Price = price * 1_000_000m,
                NormalizedPrice = price * 1_000_000m, Currency = "IRT", PublishedAt = now, PriceDay = oldCar.MarketPriceDay,
                StoredAt = now, ExtractionVersion = 5, ProcessingStatus = "Published", MatchStatus = "Matched" });
        }
        await db.SaveChangesAsync();
        var service = new TelegramPricingService(new TelegramProcessingStore(db), new TelegramListingExtractor(), new Queue(), Config(), NullLogger<TelegramPricingService>.Instance);
        await service.CalculateAsync(now, default);
        var repaired = await db.Cars.Where(x => x.Id != oldCar.Id).OrderBy(x => x.Year).ToListAsync();
        Assert.Equal(new int?[] { 1401, 1402 }, repaired.Select(x => x.Year));
        Assert.Equal(new[] { 900_000_000m, 1_000_000_000m }, repaired.Select(x => x.MarketPrice));
        Assert.Equal(950_000_000m, (await db.CarPriceHistories.SingleAsync(x => x.CarId == oldCar.Id)).MarketPrice);
        Assert.Equal("IdentityNeedsReview", oldCar.PriceReviewStatus);
        foreach (var row in await db.CarListings.Include(x => x.RawMessage).ToListAsync()) Assert.Equal(row.RawMessage!.MessageText, row.RawText);
        Assert.Equal(2, await db.Set<MarketPriceReview>().CountAsync(x => x.Action == "ExtractionRepair"));
        await service.CalculateAsync(now.AddMinutes(1), default); Assert.Equal(3, await db.CarPriceHistories.CountAsync());
    }

    [Fact]
    public async Task LegacyZeroYearsAreReextractedBeforeGroupingAndMixedLegacyGroupIsRetainedUnavailable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var now = DateTime.UtcNow;
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), new Collector(), Config(), new Market());
        await service.EnsureCatalogAsync(default);
        var model = await db.Set<ZeroVehicleModel>().Include(x => x.Trims).SingleAsync(); var trim = Assert.Single(model.Trims);
        var legacy = new ZeroVehiclePriceGroup { ModelId = model.Id, TrimId = trim.Id, ReferencePrice = 950_000_000m,
            PricingStatus = "LegacyNeedsReview", SampleCount = 2, SourceCount = 2 }; db.Add(legacy);
        foreach (var (channel, year, price) in new[] { ("dealer_one", 1401, 900), ("dealer_two", 1402, 940) }) db.Add(new ZeroVehicleListing {
            ModelId = model.Id, TrimId = trim.Id, Channel = channel, ExternalMessageId = "1", OriginalName = "تارا", ExtractionVersion = 1,
            RawText = $"تارا صفر {year} قیمت {price} میلیون تومان", PriceToman = price * 1_000_000m, PublishedAt = now,
            Fingerprint = channel, PriceCandidates = $"[{price * 1_000_000}]" });
        await db.SaveChangesAsync(); await service.RecalculateAsync(now, default);
        var groups = await db.Set<ZeroVehiclePriceGroup>().Where(x => x.PricingStatus == "Priced").OrderBy(x => x.Year).ToListAsync();
        Assert.Equal(new int?[] { 1401, 1402 }, groups.Select(x => x.Year));
        Assert.Equal(3, await db.Set<ZeroVehiclePriceGroup>().CountAsync());
        Assert.Equal(950_000_000m, legacy.ReferencePrice); Assert.Equal("Unavailable", legacy.PricingStatus);
        Assert.All(await db.Set<ZeroVehicleListing>().ToListAsync(), x => { Assert.Equal(2, x.ExtractionVersion); Assert.Equal("تارا", x.RawCarName); });
    }
}
