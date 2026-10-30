using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramPipelineTimelineTests
{
    [Fact]
    public async Task RemovingLastChannelHidesHistoricalCountersAndTimelineWithoutErasingAuditRows()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        await service.ImportAsync(new("@reset_channel"), default);
        var channel = Assert.Single(await service.GetChannelsAsync(default));
        db.TelegramRawMessages.Add(new TelegramRawMessage
        {
            ChannelId = channel.Id, ExternalMessageId = "1", MessageText = "test",
            MessageDate = DateTime.UtcNow, ProcessingStatus = "Ignored"
        });
        db.CarListings.Add(new CarListing
        {
            ChannelId = channel.Id, SourceChannel = channel.Identifier,
            SourceMessageId = "1", MatchStatus = "NeedsReview"
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, (await service.GetSummaryAsync(default)).MessagesCollected);
        Assert.Equal(1, (await service.GetSummaryAsync(default)).ListingsExtracted);
        Assert.True(await service.DeleteAsync(channel.Id, default));

        var summary = await service.GetSummaryAsync(default);
        Assert.Equal(0, summary.TotalChannels);
        Assert.Equal(0, summary.MessagesCollected);
        Assert.Equal(0, summary.ListingsExtracted);
        Assert.Equal(1, summary.StoredListingsIncludingRemovedChannels);
        Assert.Equal(0, summary.IgnoredMessages);
        var queries = new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        Assert.Equal("NotStarted", (await queries.PipelineTimelineAsync(default)).Status);
        Assert.Empty(await queries.ObservationsAsync("NeedsReview", 0, default));
        Assert.Single(await db.TelegramRawMessages.ToListAsync());
        Assert.Single(await db.CarListings.ToListAsync());
    }

    [Fact]
    public async Task TimelineCountsOnlyChannelsStillRegisteredInTheCurrentRun()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        await service.ImportAsync(new("@first_channel @other_channel"), default);
        var channels = await service.GetChannelsAsync(default);
        Assert.True(await service.DeleteAsync(channels[0].Id, default));

        var queries = new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());
        var timeline = await queries.PipelineTimelineAsync(default);
        Assert.Equal(1, timeline.TotalChannels);
        Assert.Equal("InProgress", timeline.Status);
    }

    [Fact]
    public async Task TimelineWaitsForEveryChannelAndPricingBeforeFinishing()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var queries = new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>());

        await service.ImportAsync(new("@channel_one @channel_two"), default);
        var running = await queries.PipelineTimelineAsync(default);
        Assert.Equal("InProgress", running.Status);
        Assert.Equal(2, running.TotalChannels);
        Assert.Equal("InProgress", running.Stages.Single(stage => stage.Code == "Collection").Status);
        Assert.Equal("Waiting", running.Stages.Single(stage => stage.Code == "Finished").Status);

        var jobs = await db.TelegramCollectionJobs.ToListAsync();
        jobs[0].Status = TelegramCollectionJobStatus.Succeeded;
        jobs[0].StartedAt = DateTime.UtcNow;
        jobs[0].CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal("InProgress", (await queries.PipelineTimelineAsync(default)).Status);

        jobs[1].Status = TelegramCollectionJobStatus.Failed;
        jobs[1].StartedAt = DateTime.UtcNow;
        jobs[1].CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var awaitingPricing = await queries.PipelineTimelineAsync(default);
        Assert.Equal("InProgress", awaitingPricing.Stages.Single(stage => stage.Code == "Pricing").Status);
        Assert.Equal("Waiting", awaitingPricing.Stages.Single(stage => stage.Code == "Finished").Status);

        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>()
            .CalculateAsync(DateTime.UtcNow, default);
        var finished = await queries.PipelineTimelineAsync(default);
        Assert.Equal("Partial", finished.Status);
        Assert.NotNull(finished.CompletedAt);
        Assert.Equal(1, finished.CompletedChannels);
        Assert.Equal(1, finished.FailedChannels);
        Assert.Equal("Completed", finished.Stages.Single(stage => stage.Code == "Finished").Status);
        Assert.Equal("NoData", finished.Stages.Single(stage => stage.Code == "Stored").Status);

        await service.ImportAsync(new("@channel_three"), default);
        var nextRun = await queries.PipelineTimelineAsync(default);
        Assert.Equal("InProgress", nextRun.Status);
        Assert.Null(nextRun.CompletedAt);
        Assert.Equal(1, nextRun.TotalChannels);
    }

    [Fact]
    public async Task PausingQueuedChannelDoesNotLeaveTheGlobalRunStuck()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        await service.ImportAsync(new("@paused_channel"), default);
        var channel = Assert.Single(await service.GetChannelsAsync(default));
        Assert.True(await service.SetEnabledAsync(channel.Id, false, default));

        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        Assert.Equal(TelegramCollectionJobStatus.Failed,
            (await db.TelegramCollectionJobs.SingleAsync()).Status);
        var timeline = await new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>())
            .PipelineTimelineAsync(default);
        Assert.Equal("Failed", timeline.Stages.Single(stage => stage.Code == "Collection").Status);
        Assert.Equal("InProgress", timeline.Stages.Single(stage => stage.Code == "Pricing").Status);
    }

    [Fact]
    public async Task RetriedChannelIsCountedOnceAfterSuccess()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        await service.ImportAsync(new("@retry_channel"), default);

        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var first = await db.TelegramCollectionJobs.SingleAsync();
        first.Status = TelegramCollectionJobStatus.Failed;
        first.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await service.RetryAsync(first.ChannelId, default);

        var second = await db.TelegramCollectionJobs.OrderByDescending(job => job.Id).FirstAsync();
        second.Status = TelegramCollectionJobStatus.Succeeded;
        second.StartedAt = DateTime.UtcNow;
        second.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<TelegramPricingService>()
            .CalculateAsync(DateTime.UtcNow, default);
        var timeline = await new TelegramPricingQueries(scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>())
            .PipelineTimelineAsync(default);
        Assert.Equal("Completed", timeline.Status);
        Assert.Equal(1, timeline.TotalChannels);
        Assert.Equal(1, timeline.CompletedChannels);
        Assert.Equal(0, timeline.FailedChannels);
    }
}
