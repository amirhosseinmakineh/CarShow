using System.Threading.Channels;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramCollectionQueue(IServiceScopeFactory scopeFactory) : ITelegramCollectionQueue
{
    private readonly SemaphoreSlim enqueueGate = new(1, 1);
    private readonly Channel<long> channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async ValueTask EnqueueAsync(long channelId, CancellationToken cancellationToken = default)
    {
        await enqueueGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            var source = await db.TelegramChannels.FindAsync([channelId], cancellationToken);
            if (source is null || !source.Enabled)
            {
                return;
            }

            var pendingJob = await db.TelegramCollectionJobs.AnyAsync(
                job => job.ChannelId == channelId
                    && (job.Status == TelegramCollectionJobStatus.Queued
                        || job.Status == TelegramCollectionJobStatus.Processing),
                cancellationToken);
            var state = await db.Set<TelegramPricingState>().SingleOrDefaultAsync(cancellationToken);
            if (state is null)
            {
                state = new TelegramPricingState();
                db.Add(state);
            }
            if (state.RunStartedAt is null || state.RunCompletedAt is not null)
            {
                state.RunStartedAt = DateTime.UtcNow;
                state.RunCollectionCompletedAt = null;
                state.RunCompletedAt = null;
            }
            if (!pendingJob)
            {
                db.TelegramCollectionJobs.Add(new TelegramCollectionJob
                {
                    ChannelId = channelId,
                    Status = TelegramCollectionJobStatus.Queued,
                    Attempt = source.RetryCount + 1
                });
                source.Status = TelegramChannelStatus.Pending;
                source.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);

            // SQL is the source of truth; this channel only wakes the single worker.
            await channel.Writer.WriteAsync(channelId, cancellationToken);
        }
        finally
        {
            enqueueGate.Release();
        }
    }

    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }
}
