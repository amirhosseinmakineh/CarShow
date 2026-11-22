using System.Diagnostics;

namespace CarShow.Diagnostics;

public sealed class UploadDiagnostics(ILogger<UploadDiagnostics> logger)
{
    private long started;
    public string Stage { get; private set; } = "RequestReceived";
    public long StageDurationMs => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    public void Start(string stage)
    {
        Stage = stage;
        started = Stopwatch.GetTimestamp();
        logger.LogInformation("Catalog image upload {Stage} started", Stage);
    }

    public void Complete(string outcome = "Completed")
    {
        logger.LogInformation("Catalog image upload {Stage} {Outcome} in {StageDurationMs} ms",
            Stage, outcome, StageDurationMs);
    }
}
