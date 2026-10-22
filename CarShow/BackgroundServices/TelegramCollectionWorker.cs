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
}
