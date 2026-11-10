using CarShow.Infrastracture.Context;
using Microsoft.Extensions.DependencyInjection;

namespace CarShow.BackgroundServices;

// Singleton snapshot for Swagger. The latest run is retained in memory until process restart.
public sealed class TelegramScanStatus
{
    private readonly object _gate = new();
    private object _snapshot = new { status = "Pending", totalChannels = 0 };
    public object Read() { lock (_gate) return _snapshot; }
    public void Set(object value) { lock (_gate) _snapshot = value; }
}

public sealed class TelegramScanBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(5);
    private readonly IServiceScopeFactory _scopes;
    private readonly TelegramScanStatus _status;
    private readonly ILogger<TelegramScanBackgroundService> _logger;

    public TelegramScanBackgroundService(IServiceScopeFactory scopes, TelegramScanStatus status,
        ILogger<TelegramScanBackgroundService> logger)
    {
        _scopes = scopes;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One run per host instance; no overlapping scans.
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = DateTimeOffset.UtcNow;
            _status.Set(new { status = "Running", startedAt, completedChannels = 0 });
            try
            {
                using var scope = _scopes.CreateScope();
                var scan = scope.ServiceProvider.GetRequiredService<TelegramScanService>();
                var result = await scan.ScanAsync(stoppingToken, (completed, total) =>
                    _status.Set(new { status = "Running", startedAt, completedChannels = completed, totalChannels = total }));

                var payload = result.Value;
                _status.Set(new
                {
                    status = result.StatusCode is 200 ? "Completed" :
                        result?.StatusCode is 207 ? "PartialSuccess" : "Failed",
                    startedAt,
                    finishedAt = DateTimeOffset.UtcNow,
                    result = payload
                });
                _logger.LogInformation("Scheduled Telegram scan finished with HTTP-equivalent code {Code}.", result?.StatusCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _status.Set(new { status = "Interrupted", startedAt, finishedAt = DateTimeOffset.UtcNow });
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled Telegram scan failed.");
                _status.Set(new { status = "Failed", startedAt, finishedAt = DateTimeOffset.UtcNow, error = ex.Message });
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
