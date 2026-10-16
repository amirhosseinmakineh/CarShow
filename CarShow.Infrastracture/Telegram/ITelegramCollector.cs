namespace CarShow.Infrastracture.Telegram;

public sealed record TelegramCollectionResult(bool Success, bool AccessRequired, int MessagesCollected, string? Error);
public interface ITelegramCollector
{
    Task<TelegramCollectionResult> CollectChannelAsync(string identifier, CancellationToken cancellationToken);
}
