using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramCollectionWorker(ITelegramCollectionQueue queue, IServiceScopeFactory scopeFactory, ILogger<TelegramCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var channelId in queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(channelId, stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Telegram collection job failed for channel {ChannelId}", channelId); }
        }
    }

    private async Task ProcessAsync(long channelId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        var collector = scope.ServiceProvider.GetRequiredService<ITelegramCollector>();
        var channel = await db.TelegramChannels.FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);
        if (channel is null || !channel.Enabled) return;
        channel.Status = TelegramChannelStatus.Processing;
        channel.LastAttemptAt = DateTime.UtcNow;
        channel.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var result = await collector.CollectChannelAsync(channel.NormalizedIdentifier, timeout.Token);
        channel.MessagesCollected += result.MessagesCollected;
        channel.LastError = result.Error;
        if (!result.Success) channel.RetryCount++;
        channel.Status = result.AccessRequired ? TelegramChannelStatus.AccessRequired : result.Success ? TelegramChannelStatus.Active : TelegramChannelStatus.Error;
        if (result.Success) channel.LastSuccessAt = DateTime.UtcNow;
        channel.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (!result.Success && channel.Enabled && channel.RetryCount <= 3)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, channel.RetryCount));
            await Task.Delay(delay, cancellationToken);
            await queue.EnqueueAsync(channel.Id, cancellationToken);
        }
    }
}
