using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CarShow.Infrastracture.BackgroundServices
{
    public sealed class GetCarsBackgroundService : BackgroundService
    {
        private readonly ILogger<GetCarsBackgroundService> _logger;
        private readonly SemaphoreSlim _executionLock = new(1, 1);

        public GetCarsBackgroundService(ILogger<GetCarsBackgroundService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);

                try
                {
                    await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            if (!await _executionLock.WaitAsync(0, stoppingToken))
            {
                _logger.LogWarning("Car price crawler execution skipped because another execution is still running.");
                return;
            }

            try
            {
                // Crawler implementation is intentionally deferred to CarIrCrawlerService.
                await GetCars(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Car price crawler background execution failed.");
            }
            finally
            {
                _executionLock.Release();
            }
        }

        private static Task GetCars(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _executionLock.Dispose();
            base.Dispose();
        }
    }
}
