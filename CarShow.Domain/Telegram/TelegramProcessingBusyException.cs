namespace CarShow.Domain.Telegram;

public sealed class TelegramProcessingBusyException(string resource)
    : Exception($"Telegram processing resource is busy: {resource}");
