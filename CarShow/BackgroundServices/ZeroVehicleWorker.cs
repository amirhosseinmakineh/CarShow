using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Telegram;
namespace CarShow.BackgroundServices;

public sealed class ZeroVehicleWorker(IServiceScopeFactory scopes, ILogger<ZeroVehicleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRecalculation = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            long? runId = null;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ZeroVehicleService>();
                runId = await service.ClaimAsync(DateTime.UtcNow, stoppingToken);
                if (runId.HasValue)
                {
                    // Finish or fail before the ten-minute claim lease can expire.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    await service.ProcessAsync(runId.Value, DateTime.UtcNow, timeout.Token);
                }
                else if (DateTime.UtcNow >= nextRecalculation)
                {
                    await service.RecalculateAsync(DateTime.UtcNow, stoppingToken);
                    nextRecalculation = DateTime.UtcNow.AddMinutes(15);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (TelegramProcessingBusyException) { /* Another instance owns the independent zero-vehicle lock. */ }
            catch (Exception exception)
            {
                logger.LogError(exception, "Zero vehicle run {RunId} failed", runId);
                if (runId.HasValue)
                {
                    try
                    {
                        await using var failureScope = scopes.CreateAsyncScope();
                        await failureScope.ServiceProvider.GetRequiredService<ZeroVehicleService>().FailAsync(runId.Value,
                            exception is OperationCanceledException ? "مهلت دریافت پایان یافت؛ دوباره تلاش کنید." : "پردازش خودروهای صفر ناموفق بود؛ گزارش سرور بررسی شود.", stoppingToken);
                    }
                    catch (Exception error) { logger.LogError(error, "Could not persist zero vehicle failure"); }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
