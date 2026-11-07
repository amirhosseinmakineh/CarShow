using System.Collections.Concurrent;

namespace CarShow.Services;

// On-demand scans are intentionally in memory: this stand-alone reader does not
// write to the operational database. A restart discards an unfinished scan.
public sealed class TelegramCarScanJobManager(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    ILogger<TelegramCarScanJobManager> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ScanJob> _jobs = new();
    private Guid? _runningId;

    public (Guid Id, bool Started) Start(int maxMessagesPerChannel, string? channel)
    {
        lock (_gate)
        {
            if (_runningId is { } runningId)
                return (runningId, false);

            // Keep a small bounded history of completed scans.
            foreach (var oldId in _jobs.Keys.Take(Math.Max(0, _jobs.Count - 9)).ToArray())
                _jobs.Remove(oldId);

            var job = new ScanJob(Guid.NewGuid(), DateTimeOffset.UtcNow,
                maxMessagesPerChannel, channel);
            _jobs.Add(job.Id, job);
            _runningId = job.Id;
            _ = Task.Run(() => RunAsync(job));
            return (job.Id, true);
        }
    }

    public TelegramScanStatus? GetStatus(Guid id)
    {
        ScanJob? job;
        lock (_gate)
            _jobs.TryGetValue(id, out job);

        if (job is null)
            return null;

        var channels = job.Channels.Values.OrderBy(x => x.Channel).ToArray();
        return new TelegramScanStatus(job.Id, job.State, job.StartedAt,
            job.FinishedAt, job.Channel is null ? TelegramCarService.ApprovedChannels.Count : 1,
            channels.Length, channels.Sum(x => x.MessagesProcessed),
            channels.Sum(x => x.CarsExtracted), channels);
    }

    public List<TelegramCar>? GetCars(Guid id)
    {
        ScanJob? job;
        lock (_gate)
            _jobs.TryGetValue(id, out job);

        return job is null || job.State == "Running"
            ? null
            : job.Cars.ToList();
    }

    private async Task RunAsync(ScanJob job)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<TelegramCarService>();
            IReadOnlyList<string> channels = job.Channel is null
                ? TelegramCarService.ApprovedChannels
                : [job.Channel];

            await Parallel.ForEachAsync(channels,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 12,
                    CancellationToken = applicationLifetime.ApplicationStopping
                },
                async (username, token) =>
                {
                    var messagesProcessed = 0;
                    var errors = new List<string>();
                    var cars = await scanner.GetCarsAsync(
                        job.MaxMessagesPerChannel,
                        message =>
                        {
                            logger.LogInformation("Telegram car scan: {Message}", message);
                            var prefix = username + ": ";
                            const string suffix = " messages processed.";
                            if (message.StartsWith(prefix, StringComparison.Ordinal) &&
                                message.EndsWith(suffix, StringComparison.Ordinal) &&
                                int.TryParse(message.AsSpan(prefix.Length,
                                    message.Length - prefix.Length - suffix.Length),
                                    out var count))
                                messagesProcessed = count;
                            else
                                errors.Add(message);
                        },
                        token,
                        username);

                    foreach (var car in cars)
                        job.Cars.Add(car);

                    job.Channels[username] = new TelegramChannelScanStatus(username,
                        errors.Count > 0 ? "Error" : messagesProcessed == 0 ? "NoMessages" :
                            cars.Count == 0 ? "NoMatches" : "Completed",
                        messagesProcessed, cars.Count, errors);
                });

            job.State = "Completed";
        }
        catch (OperationCanceledException) when (applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            job.State = "CanceledByRestart";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram car scan job {JobId} failed", job.Id);
            job.State = "Failed";
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            lock (_gate)
            {
                if (_runningId == job.Id)
                    _runningId = null;
            }
        }
    }

    private sealed class ScanJob(Guid id, DateTimeOffset startedAt, int maxMessagesPerChannel, string? channel)
    {
        public Guid Id { get; } = id;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public int MaxMessagesPerChannel { get; } = maxMessagesPerChannel;
        public string? Channel { get; } = channel;
        public string State { get; set; } = "Running";
        public DateTimeOffset? FinishedAt { get; set; }
        public ConcurrentDictionary<string, TelegramChannelScanStatus> Channels { get; } = new();
        public ConcurrentBag<TelegramCar> Cars { get; } = new();
    }
}

public sealed record TelegramChannelScanStatus(
    string Channel, string Status, int MessagesProcessed, int CarsExtracted,
    IReadOnlyList<string> Errors);

public sealed record TelegramScanStatus(
    Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    int TotalChannels, int CompletedChannels, int MessagesProcessed,
    int CarsExtracted, IReadOnlyList<TelegramChannelScanStatus> Channels);
