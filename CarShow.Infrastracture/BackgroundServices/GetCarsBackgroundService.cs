using CarShow.Domain.Crawling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CarShow.Infrastracture.BackgroundServices;

public sealed class GetCarsBackgroundService : BackgroundService
{
    private readonly ILogger<GetCarsBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    public GetCarsBackgroundService(ILogger<GetCarsBackgroundService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = await RunOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(succeeded ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        if (!await _executionLock.WaitAsync(0, stoppingToken))
        {
            _logger.LogWarning("Crawler execution skipped because another execution is still running.");
            return true;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var synchronizer = scope.ServiceProvider.GetRequiredService<ICarCrawlerSyncService>();
            var count = await synchronizer.SyncAsync(stoppingToken);
            _logger.LogWarning("Car.ir synchronization completed. {Count} records processed.", count);
            return count > 0;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Car.ir synchronization failed; retrying in 5 minutes.");
            return false;
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public override void Dispose()
    {
        _executionLock.Dispose();
        base.Dispose();
    }
}
