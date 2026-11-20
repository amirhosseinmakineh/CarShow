namespace CarShow.BackgroundServices;

/// <summary>
/// Executes the rejection analysis independently from queueing and HTTP state.
/// Scoped lifetime keeps the scanner and its DbContext in the same DI scope.
/// </summary>
public sealed class TelegramRejectionDiagnosticsService
{
    private readonly TelegramScanService _scanner;

    public TelegramRejectionDiagnosticsService(TelegramScanService scanner)
    {
        _scanner = scanner;
    }

    public Task<TelegramScanOutcome> RunAsync(CancellationToken cancellationToken)
        => _scanner.DiagnoseRejectionsAsync(cancellationToken);
}
