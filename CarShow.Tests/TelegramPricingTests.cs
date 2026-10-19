using CarShow.ApplicationService.Services;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.IRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramPricingTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static Car Car(bool used = false, string trim = "اتومات")
    {
        return new Car
        {
            Id = used ? 2 : 1,
            Name = "پژو 207 " + trim,
            IsUsed = used,
            Company = new Company { Name = "پژو" },
            CarModel = new CarModel { Name = "207" },
            TipId = 1,
            Tip = new Tip { Name = trim },
            MarketPrice = 100,
            FactoryPrice = 77
        };
    }

    private static CarListing Extract(string text)
    {
        return new TelegramListingExtractor().Extract(new TelegramChannel(), new TelegramRawMessage
        {
            MessageText = text,
            MessageDate = Now
        }) ?? new CarListing { RawText = text };
    }

    [Theory]
    [InlineData("پژو ۲۰۷ اتومات صفر قیمت: ۸۵۰ میلیون تومان", 850000000)]
    [InlineData("پژو ٢٠٧ اتومات صفر قیمت: ٨٥٠٠ میلیون ریال", 850000000)]
    public void MatchesCompanyModelTrimAndNormalizesMoney(string text, decimal expected)
    {
        var listing = Extract(text);
        CarListingMatcher.Match(listing, [Car()]);
        Assert.Equal("Matched", listing.MatchStatus);
        Assert.Equal(1, listing.CarId);
        Assert.Equal(expected, listing.NormalizedPrice);
    }

    [Fact]
    public void SeparatesUsedAndNewAndRejectsAmbiguousAndWrongTrim()
    {
        var listing = Extract("پژو 207 اتومات کارکرد: 2000 قیمت: 850 میلیون تومان");
        CarListingMatcher.Match(listing, [Car(), Car(true)]);
        Assert.Equal(2, listing.CarId);
        CarListingMatcher.Match(listing, [Car()]);
        Assert.Equal("Unmatched", listing.MatchStatus);
        CarListingMatcher.Match(listing, [Car(true), Car(true)]);
        Assert.Equal("Ambiguous", listing.MatchStatus);
        listing = Extract("پژو 207 دستی صفر قیمت: 850 میلیون تومان");
        CarListingMatcher.Match(listing, [Car()]);
        Assert.Equal("Unmatched", listing.MatchStatus);
    }

    [Theory]
    [InlineData("پژو 207 اتومات صفر قیمت: 850 میلیون")]
    [InlineData("پژو 207 اتومات صفر قیمت: 0 تومان")]
    [InlineData("پژو 207 اتومات صفر قیمت: -850 تومان")]
    [InlineData("پژو 207 اتومات صفر قیمت: توافقی")]
    [InlineData("پژو 207 اتومات قیمت: 850 میلیون تومان")]
    [InlineData("پژو 207 اتومات صفر کارکرد: 1000 قیمت: 850 میلیون تومان")]
    [InlineData("پژو 207 اتومات صفر قیمت: 999999999999999999999 میلیارد تومان")]
    [InlineData("پژو 207 اتومات صفر قیمت: 850 میلیون تومان قیمت: 900 میلیون تومان")]
    public void RejectsInvalidPricesOrUnknownCondition(string text)
    {
        var listing = Extract(text);
        CarListingMatcher.Match(listing, [Car()]);
        Assert.NotEqual("Matched", listing.MatchStatus);
        Assert.Null(listing.NormalizedPrice);
    }

    [Theory]
    [InlineData("بنز 207 اتومات صفر قیمت: 850 میلیون تومان")]
    [InlineData("پژو 2070 اتومات صفر قیمت: 850 میلیون تومان")]
    [InlineData("قیمت: 850 میلیون تومان صفر")]
    public void SharedNumbersAndMissingIdentityCannotMatch(string text)
    {
        var listing = Extract(text);
        CarListingMatcher.Match(listing, [Car()]);
        Assert.Equal("Unmatched", listing.MatchStatus);
        Assert.Null(listing.CarId);
    }

    [Fact]
    public void NormalizesArabicLettersSpacesAndDigits()
    {
        Assert.Equal("کی 123", CarListingMatcher.Normalize("كی\u200c ١۲3"));
    }

    [Fact]
    public void UsesTehranMidnightInsteadOfReceiptDay()
    {
        Assert.Equal(new DateOnly(2026, 10, 19), CarListingMatcher.TehranDay(new DateTime(2026, 10, 19, 20, 29, 59)));
        Assert.Equal(new DateOnly(2026, 10, 20), CarListingMatcher.TehranDay(new DateTime(2026, 10, 19, 20, 30, 0)));
    }

    [Fact]
    public async Task RealPipelineAveragesDistinctObservationsAndRerunsWithoutDuplicatingHistory()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
            var car = Car();
            car.TipId = 0;
            db.Cars.Add(car);
            var channel = new TelegramChannel { NormalizedIdentifier = "@prices" };
            var other = new TelegramChannel { NormalizedIdentifier = "@other" };
            db.TelegramChannels.AddRange(channel, other);
            await db.SaveChangesAsync();
            AddMessage(db, channel, "1", "پژو 207 اتومات صفر قیمت: 800 میلیون تومان");
            AddMessage(db, channel, "2", "پژو 207 اتومات صفر قیمت: 1000 میلیون تومان");
            AddMessage(db, other, "3", "پژو 207 اتومات صفر قیمت: 800 میلیون تومان");
            AddMessage(db, channel, "4", "پژو 207 اتومات صفر قیمت: 0 تومان");
            AddMessage(db, channel, "5", "پژو 207 اتومات صفر قیمت: 900 میلیون");
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
            var history = await db.CarPriceHistories.SingleAsync();
            Assert.Equal(900000000m, history.MarketPrice);
            Assert.Equal(2, history.SampleCount);
            Assert.Equal(77m, car.FactoryPrice);
            Assert.Equal(900000000m, car.MarketPrice);
            Assert.Equal(new DateOnly(2026, 10, 19), history.PriceDay);
            Assert.Equal(5, await db.CarListings.CountAsync());
            Assert.Equal(2, await db.CarListings.CountAsync(x => x.MatchStatus == "Invalid"));
        }

        // Fresh scope models a restart; SQL is sufficient to recover the result.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
            var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
            await service.CalculateAsync(Now.AddMinutes(5), default);
            Assert.Equal(1, await db.CarPriceHistories.CountAsync());
            Assert.Equal(5, await db.CarListings.CountAsync());
            var channel = await db.TelegramChannels.FirstAsync();
            AddMessage(db, channel, "6", "پژو 207 اتومات صفر قیمت: 1200 میلیون تومان");
            await db.SaveChangesAsync();
            await service.CalculateAsync(Now.AddMinutes(10), default);
            Assert.Equal(1000000000m, (await db.CarPriceHistories.SingleAsync()).MarketPrice);
            Assert.Equal(3, (await db.CarPriceHistories.SingleAsync()).SampleCount);
            await service.CalculateAsync(Now.AddDays(1), default);
            Assert.Equal(1, await db.CarPriceHistories.CountAsync());
            Assert.Equal(1000000000m, (await db.Cars.SingleAsync()).MarketPrice);
        }
    }

    [Fact]
    public async Task EmptyDataKeepsPreviousPriceAndLateMessageDoesNotReopenFinalDay()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
        var car = Car();
        car.TipId = 0;
        db.Cars.Add(car);
        var channel = new TelegramChannel { NormalizedIdentifier = "@prices" };
        db.TelegramChannels.Add(channel);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        Assert.Equal(100, car.MarketPrice);
        Assert.Empty(await db.CarPriceHistories.ToListAsync());
        AddMessage(db, channel, "1", "پژو 207 اتومات صفر قیمت: 800 میلیون تومان", Now.AddDays(-10));
        await db.SaveChangesAsync();
        await service.CalculateAsync(Now, default);
        Assert.Equal("Late", (await db.CarListings.SingleAsync()).MatchStatus);
        Assert.Empty(await db.CarPriceHistories.ToListAsync());
        Assert.Equal(100, car.MarketPrice);
    }

    [Fact]
    public async Task PeriodicSchedulerQueuesActiveChannelsAfterPreviousSuccess()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
        db.TelegramChannels.Add(new TelegramChannel
        {
            NormalizedIdentifier = "@prices",
            Status = TelegramChannelStatus.Active,
            LastAttemptAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.ScheduleCollectionAsync(default);
        await service.ScheduleCollectionAsync(default);
        Assert.Equal(1, await db.TelegramCollectionJobs.CountAsync());
    }

    [Fact]
    public async Task ConcurrentCalculationsUseDurableUniquenessAndOneHistory()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
            var car = Car();
            car.TipId = 0;
            db.Cars.Add(car);
            var channel = new TelegramChannel { NormalizedIdentifier = "@prices" };
            db.TelegramChannels.Add(channel);
            await db.SaveChangesAsync();
            AddMessage(db, channel, "1", "پژو 207 اتومات صفر قیمت: 800 میلیون تومان");
            await db.SaveChangesAsync();
        }
        async Task Calculate()
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        }
        await Task.WhenAll(Task.Run(Calculate), Task.Run(Calculate));
        await using var verification = fixture.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
        Assert.Equal(1, await persisted.CarPriceHistories.CountAsync());
        Assert.Equal(1, await persisted.CarListings.CountAsync());
        Assert.Equal(1, await persisted.TelegramPricingStates.CountAsync());
    }

    [Fact]
    public async Task PublicationDateWinsOverReceiptAndFinalDayCannotBeReopened()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShow.Infrastracture.Context.CarShowContext>();
        var car = Car();
        car.TipId = 0;
        db.Cars.Add(car);
        var channel = new TelegramChannel { NormalizedIdentifier = "@prices" };
        db.TelegramChannels.Add(channel);
        await db.SaveChangesAsync();
        AddMessage(db, channel, "1", "پژو 207 اتومات صفر قیمت: 800 میلیون تومان", Now.AddDays(-1));
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        Assert.Equal(new DateOnly(2026, 10, 18), (await db.CarPriceHistories.SingleAsync()).PriceDay);
        await service.CalculateAsync(Now.AddDays(3), default);
        Assert.True((await db.CarPriceHistories.SingleAsync()).IsFinal);
        AddMessage(db, channel, "2", "پژو 207 اتومات صفر قیمت: 1000 میلیون تومان", Now.AddDays(-1));
        await db.SaveChangesAsync();
        await service.CalculateAsync(Now.AddDays(3), default);
        Assert.Equal(800000000, (await db.CarPriceHistories.SingleAsync()).MarketPrice);
        Assert.Equal(1, (await db.CarPriceHistories.SingleAsync()).SampleCount);
        Assert.Equal(1, await db.CarListings.CountAsync(x => x.MatchStatus == "Late"));
    }

    private static void AddMessage(CarShow.Infrastracture.Context.CarShowContext db,
        TelegramChannel channel, string id, string text, DateTime? published = null)
    {
        db.TelegramRawMessages.Add(new TelegramRawMessage
        {
            ChannelId = channel.Id,
            ExternalMessageId = id,
            MessageText = text,
            MessageDate = published ?? Now,
            CollectedAt = Now
        });
    }
}
