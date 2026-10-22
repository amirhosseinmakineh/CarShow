using CarShow.ApplicationService.Services;
using CarShow.Domain.Telegram;

namespace CarShow.BackgroundServices;

public sealed class TelegramPricingWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramPricingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run immediately: SQL observations and jobs are the durable restart checkpoints.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
                await service.ScheduleCollectionAsync(stoppingToken);
                await service.CalculateAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TelegramProcessingBusyException)
            {
                // Another API instance is calculating; persisted results remain authoritative.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram daily pricing cycle failed; next cycle will retry persisted data.");
                try
                {
                    await using var failureScope = scopeFactory.CreateAsyncScope();
                    await failureScope.ServiceProvider.GetRequiredService<TelegramPricingService>()
                        .RecordFailureAsync("Pricing cycle failed. Check server logs for details.", stoppingToken);
                }
                catch (Exception persistenceException)
                {
                    logger.LogError(persistenceException, "Could not persist pricing failure.");
                }
            }
            var seconds = Math.Clamp(configuration.GetValue("Telegram:CalculationIntervalSeconds", 60), 10, 3600);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
    }
}
