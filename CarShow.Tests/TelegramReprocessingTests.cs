using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramReprocessingTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    private static TelegramRawMessage Message(TelegramChannel channel, string id, string text) => new()
    {
        Channel = channel, ExternalMessageId = id, MessageText = text,
        MessageDate = Now, CollectedAt = Now,
        SourceUrl = $"https://t.me/{channel.NormalizedIdentifier}/{id}",
        MediaMetadata = "https://example.test/vehicle.jpg"
    };

    [Fact]
    public async Task RepairsLegacyInvalidCandidateInPlaceAndDoesNotReparseAgain()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var raw = Message(new TelegramChannel { NormalizedIdentifier = "source_1" }, "1",
            "پژو 207 اتومات مدل: 1403 صفر قیمت: 850 میلیون تومان");
        db.TelegramRawMessages.Add(raw);
        await db.SaveChangesAsync();
        var old = new CarListing { ChannelId = raw.ChannelId, SourceMessageId = "1", RawMessageId = raw.Id,
            RawText = raw.MessageText, MatchStatus = "Invalid", Model = "old incorrect model" };
        db.CarListings.Add(old);
        await db.SaveChangesAsync();
        var id = old.Id;
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        Assert.Equal(id, old.Id);
        Assert.Equal(850000000m, old.NormalizedPrice);
        Assert.Equal("پژو", old.Manufacturer);
        Assert.Equal("207", old.Model);
        Assert.Equal("WaitingForComparableChannels", old.MatchStatus);
        Assert.Equal(TelegramListingExtractor.CurrentVersion, old.ExtractionVersion);
        Assert.Equal(raw.MessageText, old.RawText);
        Assert.Equal(raw.SourceUrl, old.SourceUrl);
        Assert.NotNull(old.PrimaryImageUrl);
        var extractedAt = old.ExtractedAt;
        await service.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(extractedAt, old.ExtractedAt);
        Assert.Equal(1, await db.CarListings.CountAsync());
        Assert.Empty(await db.Cars.ToListAsync());
        Assert.Empty(await db.CarPriceHistories.ToListAsync());
    }

    [Fact]
    public async Task KeepsNewsRawButRemovesLegacyNewsFromDefaultAdvertisementList()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var raw = Message(new TelegramChannel { NormalizedIdentifier = "news_1" }, "81307",
            "خودرو: در شرایطی هستیم که به دستور رئیس جمهور واردات خودرو فعلا متوقف شد");
        db.TelegramRawMessages.Add(raw);
        await db.SaveChangesAsync();
        db.CarListings.Add(new CarListing { ChannelId = raw.ChannelId, SourceMessageId = "81307",
            RawMessageId = raw.Id, RawText = raw.MessageText, MatchStatus = "Invalid", Model = raw.MessageText });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        var listing = await db.CarListings.SingleAsync();
        Assert.Equal("Ignored", listing.MatchStatus);
        Assert.Equal("NON_VEHICLE_MESSAGE", listing.ReasonCode);
        Assert.Null(listing.Model);
        Assert.Equal(1, await db.TelegramRawMessages.CountAsync());
        Assert.Equal(raw.MessageText, (await db.TelegramRawMessages.SingleAsync()).MessageText);
        var queries = new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        Assert.Empty(await queries.ObservationsAsync(null, 0, default));
        Assert.Single(await queries.ObservationsAsync("Ignored", 0, default));
    }

    [Fact]
    public void ActualDenaMessageWithoutPriceDoesNotTreatDeliveryDaysAsMoney()
    {
        var channel = new TelegramChannel { NormalizedIdentifier = "shakiba_iran_16" };
        var raw = Message(channel, "353294", "دنا ۶ سرعته سفید ۴۰۵ روز بدون رینگ");
        var candidate = new TelegramListingExtractor().Extract(channel, raw);
        Assert.NotNull(candidate);
        CarListingMatcher.Match(candidate, []);
        Assert.Equal("NeedsReview", candidate.MatchStatus);
        Assert.Equal("PRICE_MISSING", candidate.ReasonCode);
        Assert.Null(candidate.Price);
        Assert.Null(candidate.NormalizedPrice);
    }

    [Fact]
    public async Task ReprocessingDoesNotOverwritePreviouslyPublishedListing()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var raw = Message(new TelegramChannel { NormalizedIdentifier = "published_source" }, "1", "خبر بدون آگهی");
        db.TelegramRawMessages.Add(raw);
        await db.SaveChangesAsync();
        var stored = new CarListing { RawMessageId = raw.Id, ChannelId = raw.ChannelId, SourceMessageId = "1",
            RawText = "original advertisement", Price = 850000000m, StoredAt = Now,
            ProcessingStatus = "Published", MatchStatus = "Matched" };
        db.CarListings.Add(stored);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        Assert.Equal(850000000m, stored.Price);
        Assert.Equal("original advertisement", stored.RawText);
        Assert.Equal("Published", stored.ProcessingStatus);
    }

    [Fact]
    public void MissingConditionDoesNotMislabelAnExplicitValidPriceAsInvalid()
    {
        var channel = new TelegramChannel();
        var candidate = new TelegramListingExtractor().Extract(channel,
            Message(channel, "2", "پژو 207 اتومات مدل: 1403 قیمت: 850 میلیون تومان"))!;
        CarListingMatcher.Match(candidate, []);
        Assert.Equal(850000000m, candidate.NormalizedPrice);
        Assert.Equal("CONDITION_UNCLEAR", candidate.ReasonCode);
        Assert.Equal("NeedsReview", candidate.MatchStatus);
    }

    [Theory]
    [InlineData("۱.۳۰۰.۰۰۰.۰۰۰ تومان", 1300000000)]
    [InlineData("۱ ۳۰۰ ۰۰۰ ۰۰۰ تومان", 1300000000)]
    [InlineData("۱۳٬۰۰۰٬۰۰۰٬۰۰۰ ریال", 1300000000)]
    public void ParsesExplicitMoneyWithCommonThousandsSeparators(string price, decimal expected)
    {
        var channel = new TelegramChannel();
        var candidate = new TelegramListingExtractor().Extract(channel,
            Message(channel, "3", $"پژو 207 اتومات مدل: 1403 صفر قیمت: {price}"))!;
        CarListingMatcher.Match(candidate, []);
        Assert.Equal(expected, candidate.NormalizedPrice);
    }

    [Fact]
    public async Task SixIndependentChannelsCreateCarAndPriceInSameCycleWithoutExistingCatalog()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        for (var i = 0; i < 6; i++)
        {
            var channel = new TelegramChannel { NormalizedIdentifier = $"source_{i}" };
            db.TelegramRawMessages.Add(Message(channel, "1",
                $"پژو 207 اتومات مدل: 1403 صفر قیمت: {800 + i * 20} میلیون تومان"));
        }
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await service.CalculateAsync(Now, default);
        var car = await db.Cars.SingleAsync();
        Assert.Equal(850000000m, car.MarketPrice);
        Assert.False(car.IsUsed);
        Assert.Equal(1403, car.Year);
        Assert.Equal(6, car.PriceSampleCount);
        Assert.Equal(6, (await db.CarPriceHistories.SingleAsync()).SampleCount);
        Assert.All(await db.CarListings.ToListAsync(), x => Assert.Equal(5, x.ComparedChannelCount));
        await service.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(1, await db.Cars.CountAsync());
        Assert.Equal(1, await db.CarPriceHistories.CountAsync());
        Assert.Equal(6, await db.CarListings.CountAsync());
    }
}
