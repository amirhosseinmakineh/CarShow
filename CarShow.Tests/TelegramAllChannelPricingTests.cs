using System.Diagnostics;
using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramAllChannelPricingTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(20)]
    public async Task AllRegisteredSourcesEnterSameMeanAndCreateCarHistoryInFirstCycle(int count)
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        for (var i = 0; i < count; i++)
        {
            Add(db, new TelegramChannel { NormalizedIdentifier = $"prices_{i}" }, "1", 800 + i * 10);
        }
        // A registered channel with no comparable price never blocks valid sources.
        db.TelegramChannels.Add(new TelegramChannel { NormalizedIdentifier = "no_sample", Status = TelegramChannelStatus.Error });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        var expected = (800m + (count - 1) * 5m) * 1000000m;
        var car = await db.Cars.SingleAsync();
        var history = await db.CarPriceHistories.SingleAsync();
        Assert.Equal(expected, car.MarketPrice);
        Assert.Equal(expected, history.MarketPrice);
        Assert.Equal(count, history.SampleCount);
        Assert.Equal(count, car.PriceSampleCount);
        Assert.All(await db.CarListings.ToListAsync(), listing =>
        {
            Assert.Equal(count - 1, listing.ComparedChannelCount);
            Assert.Equal(count, listing.ValidPriceSourceCount);
            Assert.Equal(expected, listing.ComparisonAveragePrice);
            Assert.Equal("Published", listing.ProcessingStatus);
            Assert.NotNull(listing.SourceUrl);
            Assert.NotNull(listing.PrimaryImageUrl);
        });
        var observations = await new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>())
            .ObservationsAsync(null, 0, default);
        Assert.All(observations, listing => Assert.Equal(count + 1, listing.RegisteredChannelCount));
        await service.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(1, await db.Cars.CountAsync());
        Assert.Equal(1, await db.CarPriceHistories.CountAsync());
        Assert.Equal(count, await db.CarListings.CountAsync());
    }

    [Fact]
    public async Task NewSourceUpdatesExistingMeanAndHistoryWithoutDuplicateCatalogRows()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        Add(db, new TelegramChannel { NormalizedIdentifier = "first_source" }, "1", 800);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        Assert.Equal(800000000m, (await db.Cars.SingleAsync()).MarketPrice);
        Add(db, new TelegramChannel { NormalizedIdentifier = "next_source" }, "1", 1000);
        await db.SaveChangesAsync();
        await service.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(900000000m, (await db.Cars.SingleAsync()).MarketPrice);
        Assert.Equal(2, (await db.CarPriceHistories.SingleAsync()).SampleCount);
        Assert.Equal(1, await db.Companies.CountAsync());
        Assert.Equal(1, await db.CarModels.CountAsync());
        Assert.Equal(1, await db.Tips.CountAsync());
    }

    [Fact]
    public async Task SeparatesKnownAndUnknownConditionYearTrimAndDayAndExcludesDeletedSources()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var channel = new TelegramChannel { NormalizedIdentifier = "multi_source" };
        Add(db, channel, "1", 800);
        Add(db, channel, "2", 700, condition: "کارکرد: 20000");
        Add(db, channel, "3", 600, year: 1402);
        Add(db, channel, "4", 500, trim: "دستی");
        Add(db, channel, "5", 800, published: Now.AddDays(-1));
        Add(db, new TelegramChannel { NormalizedIdentifier = "deleted_source", IsDelete = true }, "1", 9000);
        var incomplete = Add(db, new TelegramChannel { NormalizedIdentifier = "incomplete" }, "1", 850);
        incomplete.MessageText = "پژو 207 اتومات مدل: 1403 قیمت: 850 میلیون تومان";
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        Assert.Equal(5, await db.Cars.CountAsync());
        Assert.Equal(6, await db.CarPriceHistories.CountAsync());
        Assert.Equal(1, await db.Cars.CountAsync(car => car.IsUsed == true));
        Assert.All(await db.CarPriceHistories.ToListAsync(), history => Assert.Equal(1, history.SampleCount));
        Assert.Equal(800000000m, (await db.Cars.SingleAsync(car => car.IsUsed == false && car.Year == 1403 && car.Tip != null && car.Tip.Name == "اتومات")).MarketPrice);
        Assert.Equal("Published", (await db.CarListings.SingleAsync(listing => listing.RawMessageId == incomplete.Id)).ProcessingStatus);
        Assert.Equal(850000000m, (await db.Cars.SingleAsync(car => car.IsUsed == null)).MarketPrice);
    }

    [Fact]
    public async Task PublicInventoryReturnsStoredNewAndUsedCarsWithNormalizedPricesAndSources()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var channel = new TelegramChannel { NormalizedIdentifier = "inventory_source" };
        Add(db, channel, "1", 800);
        Add(db, channel, "2", 700, condition: "کارکرد: 20000");
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        var service = ActivatorUtilities.CreateInstance<CarService>(scope.ServiceProvider);
        var newCars = await service.GetAllCars(null, null, null, isUsed: false);
        var usedCars = await service.GetAllCars(null, null, null, isUsed: true);
        var newCar = Assert.Single(newCars);
        var usedCar = Assert.Single(usedCars);
        Assert.False(newCar.IsUsed);
        Assert.True(usedCar.IsUsed);
        Assert.Equal(800000000m, newCar.MarketPrice);
        Assert.Equal(700000000m, usedCar.MarketPrice);
        Assert.Equal(20000, usedCar.Mileage);
        Assert.Equal(1, usedCar.PriceSampleCount);
        Assert.NotNull(newCar.SourceUrl);
        Assert.NotEmpty(newCar.ImageName);
        Assert.Equal("Compared", newCar.ComparisonStatus);
    }

    [Fact]
    public async Task CopiedAdvertisementDoesNotCreateExtraSourceWeight()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        Add(db, new TelegramChannel { NormalizedIdentifier = "original" }, "1", 800);
        Add(db, new TelegramChannel { NormalizedIdentifier = "copy" }, "1", 800);
        Add(db, new TelegramChannel { NormalizedIdentifier = "independent" }, "1", 1000);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        Assert.Equal(900000000m, (await db.Cars.SingleAsync()).MarketPrice);
        Assert.Equal(2, (await db.CarPriceHistories.SingleAsync()).SampleCount);
        Assert.Equal(1, await db.CarListings.CountAsync(listing => listing.ReasonCode == "DUPLICATE_AD"));
        await service.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(900000000m, (await db.Cars.SingleAsync()).MarketPrice);
    }

    [Fact]
    public async Task MixedCurrenciesNormalizeBeforeMeanAndUnknownIdentityCannotPublish()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        Add(db, new TelegramChannel { NormalizedIdentifier = "toman_source" }, "1", 800);
        var rial = Add(db, new TelegramChannel { NormalizedIdentifier = "rial_source" }, "1", 10000);
        rial.MessageText = rial.MessageText.Replace("تومان", "ریال");
        var unknown = Add(db, new TelegramChannel { NormalizedIdentifier = "unknown_source" }, "1", 9000);
        unknown.MessageText = "خودرو: ناشناخته تیپ: اتومات مدل: 1403 صفر قیمت: 9000 میلیون تومان";
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        Assert.Equal(900000000m, (await db.Cars.SingleAsync()).MarketPrice);
        Assert.Equal(2, (await db.CarPriceHistories.SingleAsync()).SampleCount);
        Assert.DoesNotContain(await db.CarListings.ToListAsync(), listing =>
            listing.RawMessageId == unknown.Id && listing.ProcessingStatus == "Published");
    }

    [Fact]
    public void LargeBatchUsesEveryChannelWithoutTenThousandRowTruncation()
    {
        var rows = Enumerable.Range(1, 20000).Select(index => new CarListing
        {
            Id = index,
            ChannelId = index % 200,
            SourceMessageId = index.ToString(),
            ComparisonKey = "same-verified-car",
            PriceDay = new DateOnly(2026, 10, 28),
            PublishedAt = Now.AddSeconds(index),
            NormalizedPrice = index * 1000m
        }).ToArray();
        var timer = Stopwatch.StartNew();
        var result = Assert.Single(TelegramMarketPriceCalculator.Compare(rows));
        timer.Stop();
        Assert.Equal(20000, result.Samples.Count);
        Assert.Equal(20000, result.Observations.Count);
        Assert.Equal(10000500m, result.Average);
        // Diagnostic only: machine timing is not a brittle correctness assertion.
        Console.WriteLine($"Compared {rows.Length} rows across {result.Samples.Count} channels in {timer.ElapsedMilliseconds} ms.");
    }

    private static TelegramRawMessage Add(CarShowContext db, TelegramChannel channel, string id, int millions,
        string condition = "صفر", int year = 1403, string trim = "اتومات", DateTime? published = null)
    {
        var raw = new TelegramRawMessage
        {
            Channel = channel,
            ExternalMessageId = id,
            MessageText = $"پژو 207 {trim} مدل: {year} {condition} قیمت: {millions} میلیون تومان",
            MessageDate = published ?? Now,
            CollectedAt = Now,
            SourceUrl = $"https://t.me/{channel.NormalizedIdentifier}/{id}",
            MediaMetadata = "https://example.test/car.jpg"
        };
        db.TelegramRawMessages.Add(raw);
        return raw;
    }
}
