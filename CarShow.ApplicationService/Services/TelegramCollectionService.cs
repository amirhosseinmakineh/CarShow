using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CarShow.ApplicationService.Services;

public sealed class TelegramCollectionService(
    ITelegramCollectionQueue queue,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramCollectionService> logger)
{
    public Task RecoverAsync(CancellationToken cancellationToken)
    {
        return RecoverPendingChannelsAsync(cancellationToken);
    }

    public async Task ProcessChannelAsync(long channelId, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessAsync(channelId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TelegramProcessingBusyException)
        {
            // A different instance owns the durable job; the scheduler will revisit it.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Telegram collection failed for channel {ChannelId}", channelId);
            await RecordFailureAsync(channelId, exception, cancellationToken);
            await ScheduleRetryAsync(channelId, cancellationToken);
        }
    }

    private async Task RecoverPendingChannelsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>();
        var channels = new List<TelegramChannel>();
        await db.RunExclusiveAsync("TelegramCollection", async () =>
        {
            channels = await db.Channels
                .Where(x => x.Enabled && (x.Status == TelegramChannelStatus.Pending || x.Status == TelegramChannelStatus.Processing))
                .ToListAsync(cancellationToken);
    
            var interruptedJobs = await db.Jobs
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
    
            await db.SaveAsync(cancellationToken);
        }, cancellationToken);
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
            var db = scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>();
            var channel = await db.Channels.FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);
            if (channel is null)
            {
                return;
            }

            channel.Status = channel.Enabled ? TelegramChannelStatus.Error : TelegramChannelStatus.Paused;
            channel.LastError = exception.Message;
            channel.LastAttemptAt = DateTime.UtcNow;
            channel.UpdatedAt = DateTime.UtcNow;
            channel.RetryCount++;

            var job = await db.Jobs
                .Where(x => x.ChannelId == channelId && (x.Status == TelegramCollectionJobStatus.Processing || x.Status == TelegramCollectionJobStatus.Queued))
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (job is not null)
            {
                job.Status = TelegramCollectionJobStatus.Failed;
                job.Error = exception.Message;
                job.CompletedAt = DateTime.UtcNow;
            }

            await db.SaveAsync(cancellationToken);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist Telegram collection error for channel {ChannelId}", channelId);
        }
    }

    private async Task ProcessAsync(long channelId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>();
        var collector = scope.ServiceProvider.GetRequiredService<ITelegramCollector>();
        var extractor = scope.ServiceProvider.GetRequiredService<ITelegramListingExtractor>();
        var channel = await db.Channels.FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);
        if (channel is null || !channel.Enabled)
        {
            return;
        }

        var job = await db.Jobs
            .Where(x => x.ChannelId == channelId && x.Status == TelegramCollectionJobStatus.Queued)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return;
        }

        await db.RunExclusiveAsync("TelegramCollection", async () =>
        {
            job = await db.Jobs.Where(x => x.ChannelId == channelId && x.Status == TelegramCollectionJobStatus.Queued)
                .OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
            if (job is null)
            {
                return;
            }
            job.Status = TelegramCollectionJobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            channel.Status = TelegramChannelStatus.Processing;
            channel.LastAttemptAt = DateTime.UtcNow;
            channel.UpdatedAt = DateTime.UtcNow;
            await db.SaveAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutSeconds = Math.Clamp(configuration.GetValue("Telegram:TimeoutSeconds", 120), 1, 600);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var result = await collector.CollectChannelAsync(channel.NormalizedIdentifier, timeout.Token);
            var (insertedMessages, insertedListings) = await StoreMessagesAsync(
                db, extractor, channel, result, cancellationToken);

            await ApplyCollectionResultAsync(db, channel, job, result,
                insertedMessages, insertedListings, cancellationToken);
            await db.SaveAsync(cancellationToken);
        }, cancellationToken);
        if (channel.Status == TelegramChannelStatus.Error)
        {
            await ScheduleRetryAsync(channel.Id, cancellationToken);
        }

        var delayMilliseconds = Math.Clamp(configuration.GetValue("Telegram:RequestDelayMilliseconds", 1000), 100, 60000);
        await Task.Delay(delayMilliseconds, cancellationToken);
    }

    private static async Task ApplyCollectionResultAsync(
        ITelegramProcessingStore db,
        TelegramChannel channel,
        TelegramCollectionJob job,
        TelegramCollectionResult result,
        int insertedMessages,
        int insertedListings,
        CancellationToken cancellationToken)
    {
        // A pause during network collection must not be overwritten by this scope's stale entity.
        var enabled = await db.Channels.AsNoTracking()
            .Where(x => x.Id == channel.Id)
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
            channel.RetryCount = 0;
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

    }

    private static async Task<(int Messages, int Listings)> StoreMessagesAsync(
        ITelegramProcessingStore db,
        ITelegramListingExtractor extractor,
        TelegramChannel channel,
        TelegramCollectionResult result,
        CancellationToken cancellationToken)
    {
        var insertedMessages = 0;
        var insertedListings = 0;

        foreach (var item in result.Messages)
        {
            var duplicate = await db.Messages.AnyAsync(
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
            db.Add(rawMessage);
            await db.SaveAsync(cancellationToken);
            insertedMessages++;

            var listing = extractor.Extract(channel, rawMessage);
            if (listing is not null)
            {
                var listingExists = await db.Listings.AnyAsync(
                    x => x.ChannelId == channel.Id && x.SourceMessageId == rawMessage.ExternalMessageId,
                    cancellationToken);
                if (!listingExists)
                {
                    db.Add(listing);
                    insertedListings++;
                }
                rawMessage.ProcessingStatus = "Extracted";
            }
            else
            {
                rawMessage.ProcessingStatus = "Ignored";
                rawMessage.ProcessingError = "پیام خام نگهداری شد، اما نام یک مدل خودرو برای ساخت Candidate قابل‌تأیید نبود.";
            }
        }

        return (insertedMessages, insertedListings);
    }

    private async Task ScheduleRetryAsync(long channelId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ITelegramProcessingStore>();
        var channel = await db.Channels.FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);
        var maxRetries = Math.Clamp(configuration.GetValue("Telegram:MaxRetries", 3), 0, 10);
        if (channel is null || !channel.Enabled || channel.RetryCount > maxRetries)
        {
            return;
        }

        // Persist Pending before the delay so restart recovery cannot lose a retry.
        channel.Status = TelegramChannelStatus.Pending;
        await db.SaveAsync(cancellationToken);
        var retryDelay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, channel.RetryCount)));
        await Task.Delay(retryDelay, cancellationToken);
        await queue.EnqueueAsync(channel.Id, cancellationToken);
    }
}
