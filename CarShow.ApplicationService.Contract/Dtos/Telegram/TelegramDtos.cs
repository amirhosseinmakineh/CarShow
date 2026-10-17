namespace CarShow.ApplicationService.Contract.Dtos.Telegram;

public sealed record BulkTelegramChannelImportRequest(string Input);
public sealed record AddTelegramChannelRequest(string Input);
public sealed record TelegramChannelDto(long Id, string Identifier, string Username, string? PublicUrl, string Title, string Status, bool Enabled, DateTime? LastAttemptAt, DateTime? LastSuccessAt, string? LastError, int MessagesCollected, int ListingsExtracted, int RetryCount);
public sealed record TelegramChannelSummaryDto(int TotalChannels, int Active, int Paused, int Errors, int Pending, int Processing, int MessagesCollected, int ListingsExtracted, DateTime? LastCollection);
public sealed record BulkTelegramChannelImportResult(int Total, int Created, int Duplicates, int Invalid, int Queued, IReadOnlyList<string> InvalidEntries);

public sealed record TelegramJobLogDto(long Id, long ChannelId, string Status, int Attempt, DateTime CreatedAt, DateTime? CompletedAt, string? Error);

public sealed record TelegramCapabilityDto(
    string Provider,
    bool PublicPreviewEnabled,
    bool RequiresApiCredentials,
    bool SupportsPublicUsernames,
    bool SupportsHistory,
    string Message);
