using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramCollectionWorker(
    ITelegramCollectionQueue queue,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingChannelsAsync(stoppingToken);

        await foreach (var channelId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(channelId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram collection job failed for channel {ChannelId}", channelId);
                await RecordFailureAsync(channelId, exception, stoppingToken);
                await ScheduleRetryAsync(channelId, stoppingToken);
            }
        }
    }

    private async Task RecoverPendingChannelsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var channels = await db.TelegramChannels
            .Where(x => x.Enabled && (x.Status == TelegramChannelStatus.Pending || x.Status == TelegramChannelStatus.Processing))
            .ToListAsync(cancellationToken);

        var interruptedJobs = await db.TelegramCollectionJobs
            .Where(job => job.Status == TelegramCollectionJobStatus.Processing)
            .ToListAsync(cancellationToken);
        foreach (var job in interruptedJobs)
        {
            job.Status = TelegramCollectionJobStatus.Failed;
            job.Error = "Collection interrupted by application restart.";
            job.CompletedAt = DateTime.UtcNow;
        }

        foreach (var channel in channels)
        {
            channel.Status = TelegramChannelStatus.Pending;
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var channel in channels)
        {
            await queue.EnqueueAsync(channel.Id, cancellationToken);
        }
    }

    private async Task RecordFailureAsync(long channelId, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            var channel = await db.TelegramChannels.FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);
            if (channel is null)
            {
                return;
            }

            channel.Status = channel.Enabled ? TelegramChannelStatus.Error : TelegramChannelStatus.Paused;
            channel.LastError = exception.Message;
            channel.LastAttemptAt = DateTime.UtcNow;
            channel.UpdatedAt = DateTime.UtcNow;
            channel.RetryCount++;

            var job = await db.TelegramCollectionJobs
                .Where(x => x.ChannelId == channelId && x.Status == TelegramCollectionJobStatus.Processing)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (job is not null)
            {
                job.Status = TelegramCollectionJobStatus.Failed;
                job.Error = exception.Message;
                job.CompletedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist Telegram collection error for channel {ChannelId}", channelId);
        }
    }

    private async Task ProcessAsync(long channelId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var collector = scope.ServiceProvider.GetRequiredService<ITelegramCollector>();
        var extractor = scope.ServiceProvider.GetRequiredService<ITelegramListingExtractor>();
        var channel = await db.TelegramChannels.FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);
        if (channel is null || !channel.Enabled)
        {
            return;
        }

        var job = await db.TelegramCollectionJobs
            .Where(x => x.ChannelId == channelId && x.Status == TelegramCollectionJobStatus.Queued)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = TelegramCollectionJobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        channel.Status = TelegramChannelStatus.Processing;
        channel.LastAttemptAt = DateTime.UtcNow;
        channel.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutSeconds = Math.Clamp(configuration.GetValue("Telegram:TimeoutSeconds", 120), 1, 600);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var result = await collector.CollectChannelAsync(channel.NormalizedIdentifier, timeout.Token);
        var insertedMessages = 0;
        var insertedListings = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in result.Messages)
        {
            var duplicate = await db.TelegramRawMessages.AnyAsync(
                x => x.ChannelId == channel.Id && x.ExternalMessageId == item.ExternalMessageId,
                cancellationToken);
            if (duplicate)
            {
                continue;
            }

            var rawMessage = new TelegramRawMessage
            {
                ChannelId = channel.Id,
                ExternalMessageId = item.ExternalMessageId,
                MessageText = item.Text,
                MessageDate = item.MessageDate,
                SourceUrl = item.SourceUrl,
                MediaMetadata = item.MediaMetadata,
                ProcessingStatus = "Pending"
            };
            db.TelegramRawMessages.Add(rawMessage);
            await db.SaveChangesAsync(cancellationToken);
            insertedMessages++;

            var listing = extractor.Extract(channel, rawMessage);
            if (listing is not null)
            {
                var listingExists = await db.CarListings.AnyAsync(
                    x => x.ChannelId == channel.Id && x.SourceMessageId == rawMessage.ExternalMessageId,
                    cancellationToken);
                if (!listingExists)
                {
                    db.CarListings.Add(listing);
                    insertedListings++;
                }
                rawMessage.ProcessingStatus = "Extracted";
            }
            else
            {
                rawMessage.ProcessingStatus = "Ignored";
            }
        }

        // A pause during network collection must not be overwritten by this scope's stale entity.
        var enabled = await db.TelegramChannels.AsNoTracking()
            .Where(x => x.Id == channelId)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        if (enabled is null)
        {
            return;
        }

        channel.MessagesCollected += insertedMessages;
        channel.ListingsExtracted += insertedListings;
        channel.LastError = result.Error;
        channel.UpdatedAt = DateTime.UtcNow;
        job.CompletedAt = DateTime.UtcNow;
        job.Error = result.Error;
        job.Status = result.AccessRequired
            ? TelegramCollectionJobStatus.AccessRequired
            : result.Success
                ? TelegramCollectionJobStatus.Succeeded
                : TelegramCollectionJobStatus.Failed;

        if (result.Success)
        {
            channel.Status = TelegramChannelStatus.Active;
            channel.LastSuccessAt = DateTime.UtcNow;
        }
        else
        {
            channel.RetryCount++;
            channel.Status = result.AccessRequired
                ? TelegramChannelStatus.AccessRequired
                : TelegramChannelStatus.Error;
        }

        if (!enabled.Value)
        {
            channel.Status = TelegramChannelStatus.Paused;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (!result.Success && !result.AccessRequired)
        {
            await ScheduleRetryAsync(channel.Id, cancellationToken);
        }

        var delayMilliseconds = Math.Clamp(configuration.GetValue("Telegram:RequestDelayMilliseconds", 1000), 100, 60000);
        await Task.Delay(delayMilliseconds, cancellationToken);
    }

    private async Task ScheduleRetryAsync(long channelId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var channel = await db.TelegramChannels.FindAsync([channelId], cancellationToken);
        var maxRetries = Math.Clamp(configuration.GetValue("Telegram:MaxRetries", 3), 0, 10);
        if (channel is null || !channel.Enabled || channel.RetryCount > maxRetries)
        {
            return;
        }

        // Persist Pending before the delay so restart recovery cannot lose a retry.
        channel.Status = TelegramChannelStatus.Pending;
        await db.SaveChangesAsync(cancellationToken);
        var retryDelay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, channel.RetryCount)));
        await Task.Delay(retryDelay, cancellationToken);
        await queue.EnqueueAsync(channel.Id, cancellationToken);
    }
}
