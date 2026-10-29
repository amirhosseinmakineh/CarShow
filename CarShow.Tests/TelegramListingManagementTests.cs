using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramListingManagementTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ClearHidesAllCandidatesAndPublicCarsWithoutDeletingRawOrHistory()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var channel = new TelegramChannel { NormalizedIdentifier = "clean_channel" };
        AddMessage(db, channel, "1", "پژو 207 اتومات مدل: 1403 صفر قیمت: 800 میلیون تومان");
        AddMessage(db, channel, "2", "پژو 207 اتومات مدل: 1403 قیمت: توافقی");
        await db.SaveChangesAsync();
        var pricing = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await pricing.CalculateAsync(Now, default);
        var queries = new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        var onePublished = Assert.Single(await queries.ObservationsAsync(null, 0, default));
        Assert.Equal("Published", onePublished.ProcessingStatus);
        Assert.Single(await queries.ObservationsAsync("NeedsReview", 0, default));
        var carService = ActivatorUtilities.CreateInstance<CarService>(scope.ServiceProvider);
        Assert.Single(await carService.GetAllCars(null, null, null));

        var management = new TelegramListingManagementService(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        Assert.Null(await management.ClearAsync(1, default));
        Assert.Equal(2, await db.CarListings.CountAsync(listing => !listing.IsDelete));
        var cleared = await management.ClearAsync(2, default);
        Assert.Equal(2, cleared?.ClearedCount);
        Assert.Equal(2, cleared?.PreservedRawMessages);
        Assert.Empty(await queries.ObservationsAsync(null, 0, default));
        Assert.Empty(await carService.GetAllCars(null, null, null));
        var hiddenCar = await carService.GetCarDetail(onePublished.CarId!.Value);
        Assert.False(hiddenCar.IsSuccess);
        Assert.Equal(2, await db.TelegramRawMessages.CountAsync());
        Assert.Equal(1, await db.CarPriceHistories.CountAsync());
        Assert.All(await db.TelegramRawMessages.ToListAsync(), message => Assert.Equal("Cleared", message.ProcessingStatus));
        await pricing.CalculateAsync(Now.AddMinutes(1), default);
        Assert.Equal(2, await db.CarListings.CountAsync());
        Assert.All(await db.CarListings.ToListAsync(), listing => Assert.True(listing.IsDelete));
    }

    [Fact]
    public async Task TimelineUsesPersistedJobAndStageTimesWithoutClaimingMissingStorage()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var channel = new TelegramChannel { NormalizedIdentifier = "timeline_channel" };
        var raw = AddMessage(db, channel, "1", "پژو 207 اتومات مدل: 1403 صفر قیمت: 800 میلیون تومان");
        db.TelegramCollectionJobs.Add(new TelegramCollectionJob
        {
            Channel = channel,
            CreatedAt = Now.AddMinutes(-2),
            StartedAt = Now.AddMinutes(-1),
            CompletedAt = Now.AddMinutes(1),
            Status = TelegramCollectionJobStatus.Succeeded
        });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>().CalculateAsync(Now, default);
        var queries = new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        var job = await db.TelegramCollectionJobs.SingleAsync();
        var timeline = await queries.TimelineAsync(job.Id, default);
        Assert.NotNull(timeline);
        Assert.Equal(1, timeline.Stages.Single(stage => stage.Code == "Received").Count);
        Assert.Equal(raw.CollectedAt, timeline.Stages.Single(stage => stage.Code == "Received").OccurredAt);
        Assert.Equal("Completed", timeline.Stages.Single(stage => stage.Code == "Stored").Status);
        Assert.Equal(1, timeline.Stages.Single(stage => stage.Code == "Stored").Count);
        var management = new TelegramListingManagementService(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        await management.ClearAsync(1, default);
        var preservedTimeline = await queries.TimelineAsync(job.Id, default);
        Assert.Equal(1, preservedTimeline!.Stages.Single(stage => stage.Code == "Stored").Count);
        Assert.Null(await queries.TimelineAsync(long.MaxValue, default));
    }

    private static TelegramRawMessage AddMessage(CarShowContext db, TelegramChannel channel, string id, string text)
    {
        var raw = new TelegramRawMessage
        {
            Channel = channel,
            ExternalMessageId = id,
            MessageText = text,
            MessageDate = Now,
            CollectedAt = Now,
            SourceUrl = $"https://t.me/{channel.NormalizedIdentifier}/{id}",
            MediaMetadata = "https://example.test/car.jpg"
        };
        db.TelegramRawMessages.Add(raw);
        return raw;
    }
}
