namespace CarShow.Domain.Telegram;

public sealed record TelegramCollectedMessage(string ExternalMessageId, string Text, DateTime MessageDate, string? SourceUrl, string? MediaMetadata);
public sealed record TelegramCollectionResult(bool Success, bool AccessRequired, IReadOnlyList<TelegramCollectedMessage> Messages, string? Error,
    int Requested = 0, int PagesFetched = 0, string StopReason = "", long? NextBefore = null);

public interface ITelegramCollector
{
    Task<TelegramCollectionResult> CollectChannelAsync(string identifier, CancellationToken cancellationToken);
    Task<TelegramCollectionResult> CollectChannelAsync(string identifier, int messageLimit, long? before, CancellationToken cancellationToken)
        => CollectChannelAsync(identifier, cancellationToken);
}
