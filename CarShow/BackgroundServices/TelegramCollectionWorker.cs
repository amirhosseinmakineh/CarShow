using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Telegram;

namespace CarShow.BackgroundServices;

public sealed class TelegramCollectionWorker(
    ITelegramCollectionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TelegramCollectionService>().RecoverAsync(stoppingToken);
                break;
            }
            catch (TelegramProcessingBusyException)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Revisit expired claims even when the queue is empty after a crash.
        using var recoveryStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var recovery = RecoverExpiredAsync(recoveryStop.Token);
        try
        {
            await foreach (var channelId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<TelegramCollectionService>()
                        .ProcessChannelAsync(channelId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Telegram worker failed for {ChannelId}", channelId);
                }
            }
        }
        finally { await recoveryStop.CancelAsync(); await recovery; }
    }

    private async Task RecoverExpiredAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<TelegramCollectionService>().RecoverAsync(cancellationToken);
                }
                catch (TelegramProcessingBusyException) { /* Another instance is writing. */ }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Telegram claim recovery failed");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
