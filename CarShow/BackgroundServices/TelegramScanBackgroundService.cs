using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace CarShow.BackgroundServices;

public sealed class TelegramScanStatus
{
    private readonly string _connectionString;
    public TelegramScanStatus(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("CarShow")
            ?? throw new InvalidOperationException("CarShow connection string missing.");
    }

    public SqlConnection CreateConnection() => new(_connectionString);

    // Additive, guarded schema change; no Cars or existing tables are altered.
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.TelegramScanJobs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TelegramScanJobs (
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    Status nvarchar(32) NOT NULL,
                    StartedAt datetimeoffset NOT NULL,
                    FinishedAt datetimeoffset NULL,
                    TotalChannels int NOT NULL DEFAULT 0,
                    CompletedChannels int NOT NULL DEFAULT 0,
                    FailedChannels int NOT NULL DEFAULT 0,
                    MessagesProcessed int NOT NULL DEFAULT 0,
                    ParsedCars int NOT NULL DEFAULT 0,
                    ReferenceCount int NOT NULL DEFAULT 0,
                    SavedCars int NOT NULL DEFAULT 0,
                    ChannelErrors nvarchar(max) NOT NULL DEFAULT N'{}',
                    Error nvarchar(max) NULL
                );
            END
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> StartAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.TelegramScanJobs (Status, StartedAt)
            OUTPUT INSERTED.Id VALUES (N'Running', SYSDATETIMEOFFSET());
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    public async Task UpdateAsync(long id, string status, int completed, TelegramScanOutcome? result,
        string? error, CancellationToken ct)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.TelegramScanJobs SET
                Status = @status,
                FinishedAt = CASE WHEN @status = N'Running' THEN NULL ELSE SYSDATETIMEOFFSET() END,
                TotalChannels = @total,
                CompletedChannels = @completed,
                FailedChannels = @failed,
                MessagesProcessed = @messages,
                ParsedCars = @parsed,
                ReferenceCount = @references,
                SavedCars = @saved,
                ChannelErrors = @errors,
                Error = @error
            WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@total", result?.TotalChannels ?? 0);
        command.Parameters.AddWithValue("@completed", completed);
        command.Parameters.AddWithValue("@failed", result?.FailedChannels ?? 0);
        command.Parameters.AddWithValue("@messages", result?.MessagesProcessed ?? 0);
        command.Parameters.AddWithValue("@parsed", result?.ParsedCars ?? 0);
        command.Parameters.AddWithValue("@references", result?.ReferenceCount ?? 0);
        command.Parameters.AddWithValue("@saved", result?.SavedCars ?? 0);
        command.Parameters.AddWithValue("@errors", JsonSerializer.Serialize(result?.ChannelErrors ?? new Dictionary<string, string>()));
        command.Parameters.Add("@error", SqlDbType.NVarChar, -1).Value = (object?)error ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<object> ReadAsync(CancellationToken ct)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) Status, StartedAt, FinishedAt, TotalChannels,
                CompletedChannels, FailedChannels, MessagesProcessed, ParsedCars,
                ReferenceCount, SavedCars, ChannelErrors, Error
            FROM dbo.TelegramScanJobs ORDER BY Id DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new { status = "Pending" };
        return new
        {
            status = reader.GetString(0),
            startedAt = reader.GetDateTimeOffset(1),
            finishedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(2),
            totalChannels = reader.GetInt32(3),
            completedChannels = reader.GetInt32(4),
            failedChannels = reader.GetInt32(5),
            messagesProcessed = reader.GetInt32(6),
            parsedCars = reader.GetInt32(7),
            referenceCount = reader.GetInt32(8),
            savedCars = reader.GetInt32(9),
            channelErrors = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(10)),
            error = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _status.EnsureSchemaAsync(stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Telegram scan worker failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        // Keep this dedicated SQL session open throughout the scan. SQL Server releases
        // the exclusive application lock when the connection closes, including on crashes.
        await using var lockConnection = _status.CreateConnection();
        await lockConnection.OpenAsync(ct);
        await using var acquire = lockConnection.CreateCommand();
        acquire.CommandText = """
            DECLARE @code int;
            EXEC @code = sys.sp_getapplock
                @Resource = N'CarShow.TelegramScan',
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0;
            SELECT @code;
            """;
        var code = Convert.ToInt32(await acquire.ExecuteScalarAsync(ct));
        if (code < 0)
        {
            _logger.LogInformation("Telegram scan skipped: another instance holds the lock ({Code}).", code);
            return;
        }

        long jobId = 0;
        try
        {
            jobId = await _status.StartAsync(lockConnection, ct);
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TelegramScanService>();
            int completed = 0;
            int total = 0;
            var result = await service.ScanAsync(ct, (done, count) =>
            {
                Volatile.Write(ref completed, done);
                Volatile.Write(ref total, count);
            });
            var finalStatus = result.StatusCode == 200 ? "Completed"
                : result.StatusCode == 207 ? "PartialSuccess" : "Failed";
            await _status.UpdateAsync(jobId, finalStatus, completed, result, null, ct);
            _logger.LogInformation("Telegram scan {JobId}: {Status}, {SavedCars} reference cars saved.",
                jobId, finalStatus, result.SavedCars);
        }
        catch (Exception ex)
        {
            if (jobId != 0)
            {
                try { await _status.UpdateAsync(jobId, "Failed", 0, null, ex.Message, CancellationToken.None); }
                catch (Exception updateError) { _logger.LogError(updateError, "Could not persist scan failure."); }
            }
            throw;
        }
        finally
        {
            try
            {
                await using var release = lockConnection.CreateCommand();
                release.CommandText = """
                    EXEC sys.sp_releaseapplock @Resource = N'CarShow.TelegramScan', @LockOwner = 'Session';
                    """;
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "SQL session lock release failed; closing session."); }
        }
    }
}
