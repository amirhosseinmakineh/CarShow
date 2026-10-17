namespace CarShow.Infrastracture.Telegram;

public sealed record TelegramCollectedMessage(string ExternalMessageId, string Text, DateTime MessageDate, string? SourceUrl, string? MediaMetadata);
public sealed record TelegramCollectionResult(bool Success, bool AccessRequired, IReadOnlyList<TelegramCollectedMessage> Messages, string? Error);

public interface ITelegramCollector
{
    Task<TelegramCollectionResult> CollectChannelAsync(string identifier, CancellationToken cancellationToken);
}
