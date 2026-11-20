using System.Threading.Channels;

namespace CarShow.BackgroundServices;

// Long Telegram requests must not be tied to an HTTP connection lifetime.
public sealed class TelegramRejectionDiagnosticsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TelegramRejectionDiagnosticsWorker> _logger;
    private readonly Channel<bool> _queue = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = false });
    private readonly object _gate = new();
    private bool _busy;
    private string _state = "Pending";
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;
    private object? _result;
    private string? _error;

    public TelegramRejectionDiagnosticsWorker(IServiceScopeFactory scopes,
        ILogger<TelegramRejectionDiagnosticsWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public bool TryQueue()
    {
        lock (_gate)
        {
            if (_busy) return false;
            _busy = true;
            _state = "Queued";
            _startedAt = null;
            _finishedAt = null;
            _result = null;
            _error = null;
            if (_queue.Writer.TryWrite(true)) return true;
            _busy = false;
            _state = "Pending";
            return false;
        }
    }

    public object ReadStatus()
    {
        lock (_gate)
            return new { status = _state, startedAt = _startedAt,
                finishedAt = _finishedAt, error = _error };
    }

    public object ReadResults()
    {
        lock (_gate)
            return new { status = _state, startedAt = _startedAt,
                finishedAt = _finishedAt, results = _result, error = _error };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                lock (_gate)
                {
                    _state = "Running";
                    _startedAt = DateTimeOffset.UtcNow;
                }
                try
                {
                    using var scope = _scopes.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<TelegramRejectionDiagnosticsService>();
                    var outcome = await service.RunAsync(stoppingToken);
                    lock (_gate)
                    {
                        _result = outcome.Value;
                        _state = outcome.StatusCode is >= 200 and < 300 ? "Completed" : "Failed";
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    lock (_gate) { _state = "Cancelled"; _error = "Server is shutting down."; }
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Telegram rejection diagnostics failed.");
                    lock (_gate) { _state = "Failed"; _error = ex.Message; }
                }
                finally
                {
                    lock (_gate)
                    {
                        _finishedAt = DateTimeOffset.UtcNow;
                        _busy = false;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
