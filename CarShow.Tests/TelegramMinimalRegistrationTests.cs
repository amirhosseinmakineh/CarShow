using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramMinimalRegistrationTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("شاهین قیمت: ۹۸۰ میلیون تومان", null, null)]
    [InlineData("شاهین قیمت: ۹۸۰,۰۰۰,۰۰۰", null, null)]
    [InlineData("شاهین مدل: 1403 قیمت: ۹۸۰ میلیون تومان", 1403, null)]
    [InlineData("شاهین صفر قیمت: ۹۸۰ میلیون تومان", null, false)]
    [InlineData("شاهین کارکرده قیمت: ۹۸۰ میلیون تومان", null, true)]
    public async Task NameAndPricePublishCarAndDailyHistoryWithoutOptionalFields(string text, int? year, bool? used)
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var raw = AddMessage(db, "minimal", text);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);

        var car = await db.Cars.SingleAsync();
        var listing = await db.CarListings.SingleAsync();
        var history = await db.CarPriceHistories.SingleAsync();
        Assert.Equal(980000000m, car.MarketPrice);
        Assert.Equal(year, car.Year);
        Assert.Equal(used, car.IsUsed);
        Assert.Null(car.CompanyId);
        Assert.Null(car.TipId);
        Assert.Empty(car.ImageName);
        Assert.Equal("Published", listing.ProcessingStatus);
        Assert.Null(listing.ReasonCode);
        Assert.Equal(car.Id, listing.CarId);
        Assert.Equal("Stored", raw.ProcessingStatus);
        Assert.Equal(980000000m, history.MarketPrice);
        Assert.Equal(1, history.SampleCount);
        Assert.Equal("Low", history.ConfidenceLevel);
        var observations = await new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>())
            .ObservationsAsync(null, 0, default);
        Assert.Single(observations);
        var inventory = ActivatorUtilities.CreateInstance<CarService>(scope.ServiceProvider);
        var publicCar = Assert.Single(await inventory.GetAllCars(null, null, null));
        Assert.Equal(used, publicCar.IsUsed);
        if (used is null)
        {
            Assert.Empty(await inventory.GetAllCars(null, null, null, isUsed: false));
            Assert.Empty(await inventory.GetAllCars(null, null, null, isUsed: true));
        }

        await service.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(1, await db.Cars.CountAsync());
        Assert.Equal(1, await db.CarPriceHistories.CountAsync());
        Assert.Equal(1, await db.CarListings.CountAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(used, (await db.Cars.SingleAsync()).IsUsed);
    }

    [Fact]
    public async Task OldAdsWithNameAndPriceAreStoredWithoutSourceLinksOrPriceRangeGate()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var first = AddMessage(db, "first", "شاهین قیمت: ۸۰۰ میلیون تومان");
        var second = AddMessage(db, "second", "شاهین فروخته شد قیمت: ۹۰۰ میلیون تومان");
        var third = AddMessage(db, "third", "شاهین قیمت: ۹۰۰۰ میلیون تومان");
        foreach (var message in new[] { first, second, third })
        {
            message.MessageDate = Now.AddDays(-10);
            message.SourceUrl = null!;
        }
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        db.ChangeTracker.Clear();

        var car = await db.Cars.SingleAsync();
        var listings = await db.CarListings.ToListAsync();
        var history = await db.CarPriceHistories.SingleAsync();
        Assert.Equal(3, listings.Count);
        Assert.All(listings, listing =>
        {
            Assert.Equal("Published", listing.ProcessingStatus);
            Assert.Equal(car.Id, listing.CarId);
        });
        Assert.Equal(decimal.Round((800000000m + 900000000m + 9000000000m) / 3m, 2), car.MarketPrice);
        Assert.Equal(car.MarketPrice, history.MarketPrice);
        Assert.Equal(3, history.SampleCount);
    }

    [Fact]
    public async Task ReprocessesVersionFourNeedsReviewCandidateInPlace()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var raw = AddMessage(db, "legacy", "شاهین قیمت: ۹۸۰ میلیون تومان");
        raw.ExtractionVersion = 4;
        raw.ProcessingStatus = "NeedsReview";
        await db.SaveChangesAsync();
        var listing = new CarListing
        {
            ChannelId = raw.ChannelId, RawMessageId = raw.Id, SourceMessageId = raw.ExternalMessageId,
            RawText = raw.MessageText, ExtractionVersion = 4,
            MatchStatus = "NeedsReview", ProcessingStatus = "NeedsReview", ReasonCode = "YEAR_MISSING"
        };
        db.CarListings.Add(listing);
        await db.SaveChangesAsync();
        var id = listing.Id;
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);

        Assert.Equal(id, (await db.CarListings.SingleAsync()).Id);
        Assert.Equal("Published", listing.ProcessingStatus);
        Assert.Equal(TelegramListingExtractor.CurrentVersion, raw.ExtractionVersion);
        Assert.Equal(980000000m, (await db.Cars.SingleAsync()).MarketPrice);
        Assert.Single(await db.CarPriceHistories.ToListAsync());
    }

    [Fact]
    public async Task UnknownFieldsHaveSeparateIdentityAndMultipleSourcesCanAverage()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        AddMessage(db, "one", "شاهین قیمت: ۸۰۰ میلیون تومان");
        AddMessage(db, "two", "شاهین قیمت: ۱۰۰۰ میلیون تومان");
        AddMessage(db, "new", "شاهین مدل: 1403 صفر قیمت: ۱۲۰۰ میلیون تومان");
        AddMessage(db, "used", "شاهین مدل: 1403 کارکرده قیمت: ۷۰۰ میلیون تومان");
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);

        Assert.Equal(3, await db.Cars.CountAsync());
        var unknown = await db.Cars.SingleAsync(car => car.IsUsed == null);
        Assert.Null(unknown.Year);
        Assert.Equal(900000000m, unknown.MarketPrice);
        Assert.Equal(2, unknown.PriceSampleCount);
        Assert.Equal(1200000000m, (await db.Cars.SingleAsync(car => car.IsUsed == false)).MarketPrice);
        Assert.Equal(700000000m, (await db.Cars.SingleAsync(car => car.IsUsed == true)).MarketPrice);
        Assert.All(await db.CarListings.ToListAsync(), listing => Assert.Equal("Published", listing.ProcessingStatus));
    }

    private static TelegramRawMessage AddMessage(CarShowContext db, string channel, string text)
    {
        var raw = new TelegramRawMessage
        {
            Channel = new TelegramChannel { NormalizedIdentifier = channel },
            ExternalMessageId = "1", MessageText = text, MessageDate = Now, CollectedAt = Now,
            SourceUrl = $"https://t.me/{channel}/1"
        };
        db.TelegramRawMessages.Add(raw);
        return raw;
    }
}
