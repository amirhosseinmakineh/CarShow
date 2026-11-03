using System.Text;
using System.Text.Json;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.ZeroVehicles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CarShow.Tests;

public sealed class ZeroMarketPipelineTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private sealed class Collector : ITelegramCollector
    {
        public List<TelegramCollectedMessage> Messages = [];
        public Task<TelegramCollectionResult> CollectChannelAsync(string channel, CancellationToken ct)
            => Task.FromResult(new TelegramCollectionResult(true, false, Messages, null));
    }
    private sealed class Market : ICarIrMarketPriceCrawler
    {
        public MarketPriceResult Result = new("Matched", "تارا", 3_000_000_000m, Now, Now, "https://car.ir/example");
        public Task<MarketPriceResult> LookupAsync(string name, CancellationToken ct) => Task.FromResult(Result);
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
        """{"ZeroVehicles":{"Catalog":[{"Name":"تارا","Aliases":[],"Trims":[]}]}}"""))).Build();
    [Fact]
    public async Task SuspiciousFirstQuoteDoesNotBlockOtherChannelsAndReplayIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collector = new Collector(); var market = new Market();
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), market);
        async Task Import(string channel, string body)
        {
            collector.Messages = [new("1", body, Now, "https://t.me/" + channel + "/1", null)];
            var run = await service.EnqueueAsync(new(channel), default); await service.ClaimAsync(Now, default);
            await service.ProcessAsync(run.Id, Now, default);
        }
        await Import("channel_a", "تارا صفر ۱۲۰۰م");
        var group = await db.Set<ZeroVehiclePriceGroup>().SingleAsync();
        Assert.Equal("ReferenceOnly", group.PricingStatus); Assert.Equal(3_000_000_000m, group.ReferencePrice);
        await Import("channel_b", "تارا صفر ۲۹۵۰م");
        await Import("channel_c", "تارا صفر ۳۰۵۰م");
        await Import("channel_d", "تارا صفر ۳۰۰۰م");
        Assert.Equal(3_000_000_000m, group.ReferencePrice); Assert.Equal(3, group.SampleCount);
        Assert.Equal("Outlier", (await db.Set<ZeroVehicleListing>().SingleAsync(x => x.Channel == "channel_a")).PricingStatus);
        await Import("channel_b", "تارا صفر ۲۹۵۰م");
        Assert.Equal(4, await db.Set<ZeroVehicleListing>().CountAsync());
        market.Result = new("SourceUnavailable", Reason: "NetworkError");
        await service.RecalculateAsync(Now, default);
        Assert.Empty(await db.Set<ZeroVehiclePriceGroup>().ToListAsync());
        Assert.All(await db.Set<ZeroVehicleListing>().ToListAsync(), x => Assert.Null(x.FinalPrice));
        Assert.Empty(await db.Cars.ToListAsync()); Assert.Empty(await db.CarListings.ToListAsync());
    }
    [Fact]
    public async Task BulkChannelsDeduplicateAndInvalidIdentifierDoesNotDiscardOtherChannels()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), new Collector(), Config(), new Market());
        var added = await service.AddChannelsAsync("@channel_a\nhttps://t.me/channel_a\nbad!\nchannel_b\nchannel_c", default);
        Assert.Single(added, x => x.Error is not null); Assert.Equal(3, (await service.ChannelsAsync(default)).Count);
        var b = (await service.ChannelsAsync(default)).Single(x => x.Identifier == "channel_b");
        await service.ChangeChannelAsync(b.Id, false, default);
        Assert.Equal(2, (await service.ImportAllAsync(100, default)).Count);
        var replay = await service.ImportAllAsync(100, default); Assert.All(replay, x => Assert.NotNull(x.Error));
        Assert.Equal(2, await db.Set<ZeroVehicleImportRun>().CountAsync());
    }
    [Fact]
    public async Task NumberedMessagesPersistEveryZeroPartAndRejectPositiveMileageIndividually()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collector = new Collector { Messages = [new("42", "موجودی خودرو صفر\n1️⃣تارا صفر ۳۰۰۰م\n2⃣تارا کارکرد ۱۶ هزار ۲۹۰۰م\n3- تارا بدون کارکرد ۲۹۵۰م", Now, "https://t.me/channel_a/42", null)] };
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), new Market());
        async Task<CarShow.ApplicationService.Contract.Dtos.ZeroVehicles.ZeroImportDto> Run()
        {
            var queued = await service.EnqueueAsync(new("channel_a"), default);
            await service.ClaimAsync(Now, default); await service.ProcessAsync(queued.Id, Now, default);
            return (await service.GetRunAsync(queued.Id, default))!;
        }
        var first = await Run();
        Assert.Equal(1, first.Scanned); Assert.Equal(3, first.Extracted); Assert.Equal(2, first.Stored); Assert.Equal(1, first.Rejected);
        Assert.Equal(new[] { 0, 2 }, await db.Set<ZeroVehicleListing>().OrderBy(x => x.PartIndex).Select(x => x.PartIndex).ToArrayAsync());
        var repeated = await Run(); Assert.Equal(2, repeated.Duplicates); Assert.Equal(0, repeated.Stored);
        Assert.Empty(await db.Set<ZeroVehicleIssue>().ToListAsync());
    }
    [Fact]
    public async Task ToleranceSettingsPersistAndRecalculationUsesThem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collector = new Collector { Messages = [new("1", "تارا صفر ۲۶۰۰م", Now, null, null)] };
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config(), new Market());
        var run = await service.EnqueueAsync(new("channel_a"), default); await service.ClaimAsync(Now, default); await service.ProcessAsync(run.Id, Now, default);
        Assert.Equal("Included", (await db.Set<ZeroVehicleListing>().SingleAsync()).PricingStatus);
        Assert.Equal(10, await service.SettingsAsync(10, default)); await service.RecalculateAsync(Now, default);
        Assert.Equal("Outlier", (await db.Set<ZeroVehicleListing>().SingleAsync()).PricingStatus);
        Assert.Equal("ReferenceOnly", (await db.Set<ZeroVehiclePriceGroup>().SingleAsync()).PricingStatus);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SettingsAsync(0, default));
    }
    [Theory]
    [InlineData("۲۸۸۰م", 2880000000)]
    [InlineData("۲ میلیارد و ۸۸۰ میلیون", 2880000000)]
    [InlineData("2,880,000,000 تومان", 2880000000)]
    [InlineData("٢٨٨٠م", 2880000000)]
    public void ExtractsFullAmountWithMixedDigits(string text, long expected)
        => Assert.Equal((decimal)expected, Assert.Single(ZeroVehicleRules.ExtractPrice(text, new()).Candidates));
    [Fact]
    public void AncillaryCostIsNotTheVehiclePrice()
    {
        var result = ZeroVehicleRules.ExtractPrice("پژو ۲۰۷ صفر\nکارت طلایی ۲۴م تسویه شد\n۲۸۸۰م", new());
        Assert.Equal(2_880_000_000m, Assert.Single(result.Candidates));
        Assert.Equal(2_880_000_000m, Assert.Single(ZeroVehicleRules.ExtractPrice("پژو ۲۰۷ بیمه تا ۱۴۰۶/۰۷ صفر ۲۸۸۰م", new()).Candidates));
    }
    [Theory]
    [InlineData("پیش پرداخت ۲ میلیارد")][InlineData("قسط ماهانه ۲۴م")][InlineData("تارا صفر تماس 09123456789")]
    public void PaymentAndPhoneNumbersDoNotProduceFullPrices(string text)
        => Assert.Empty(ZeroVehicleRules.ExtractPrice(text, new()).Candidates);
    [Theory]
    [InlineData("صفر کارکرد ۱۶هزار")][InlineData("صفر کارکرد ۱۱ هزار")][InlineData("۱۶هزار کارکرد")][InlineData("بدون کارکرد ۱۶ هزار کیلومتر")][InlineData("بدون کارکرد؛ کارکرد ۱۶ هزار")]
    public void PositiveMileageOverridesZero(string text) => Assert.NotNull(ZeroVehicleRules.ZeroFailure(text));
    [Fact]
    public void MixedNumberingAndMultilineBodiesStaySeparate()
    {
        // Synthetic format coverage; this is deliberately NOT the missing original ten-car sample.
        const string text = "موجودی خودرو صفر\n1️⃣پژو ۲۰۷ صفر ۲۸۸۰م\nکارت طلایی ۲۴م\n2⃣تارا صفر ۴۴۹۰م\n3- اطلس صفر ۲۰۴۵م\n۴. لاماری صفر ۶.۲م\n۵) پژو ۲۰۷ صفر ۲۸۴۵م\n1️⃣0️⃣لاماری صفر ۵.۲م";
        var parts = ZeroVehicleRules.Split(text); Assert.Equal(6, parts.Count);
        Assert.Contains("کارت طلایی", parts[0].Text); Assert.DoesNotContain("کارت طلایی", parts[1].Text);
        Assert.Single(ZeroVehicleRules.Split("تارا صفر\n2.045 میلیارد تومان"));
        Assert.All(parts.Select((x, i) => (x, i)), p => Assert.Equal(p.i, p.x.Index));
        Assert.Null(ZeroVehicleRules.ZeroFailure(parts[0].Text));
        Assert.NotNull(ZeroVehicleRules.ZeroFailure("تارا ۳۰۰۰م"));
    }
    [Fact]
    public void RealRecordedCarIrTableSeparatesMarketFactoryAndUnknownAge()
    {
        var entries = CarIrMarketPriceCrawler.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/carir-prices-20261102.html")), Now);
        Assert.Contains(entries, x => x.Name.Contains("M5") && x.Price == 14_000_000_000m);
        Assert.DoesNotContain(entries, x => x.Price == 7_449_000_000m);
        Assert.Contains(entries, x => x.Name.Contains("جنسیس") && x.Price is null);
        Assert.Equal("Ambiguous", CarIrMarketPriceCrawler.Match("آیتو M5", entries, Now, 7).Status);
        Assert.Equal("Matched", CarIrMarketPriceCrawler.Match("آیتو M5 مدل 2025", entries, Now, 7).Status);
        Assert.Null(CarIrMarketPriceCrawler.ParseAge("نامشخص", Now));
        Assert.True(CarIrMarketPriceCrawler.AllowsPrices("User-agent: *\nDisallow: /prices/get-price/*"));
        Assert.False(CarIrMarketPriceCrawler.AllowsPrices("User-agent: *\nDisallow: /prices"));
    }
}
