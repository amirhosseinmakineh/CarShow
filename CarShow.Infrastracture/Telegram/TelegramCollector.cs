using TL;
using WTelegram;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramCollector(IConfiguration configuration, ILogger<TelegramCollector> logger) : ITelegramCollector, IAsyncDisposable
{
    private readonly SemaphoreSlim loginGate = new(1, 1);
    private Client? client;

    public async Task<TelegramCollectionResult> CollectChannelAsync(string identifier, CancellationToken cancellationToken)
    {
        if (!long.TryParse(configuration["Telegram:ApiId"], out _) || string.IsNullOrWhiteSpace(configuration["Telegram:ApiHash"]))
        {
            return AccessRequired("Telegram ApiId/ApiHash are not configured.");
        }

        if (!identifier.StartsWith("@", StringComparison.Ordinal)) identifier = $"@{identifier}";
        if (identifier.StartsWith("@-100", StringComparison.Ordinal))
        {
            return AccessRequired("Numeric channel identifiers require a resolved MTProto peer. Store a public username or configure peer resolution.");
        }

        try
        {
            await EnsureConnectedAsync(cancellationToken);
            var peer = await client!.Contacts_ResolveUsername(identifier[1..]);
            var historyLimit = GetHistoryLimit();
            var history = await client.Messages_GetHistory(peer, limit: historyLimit);
            var messages = history.Messages
                .OfType<Message>()
                .Where(message => !string.IsNullOrWhiteSpace(message.message))
                .Select(message => new TelegramCollectedMessage(
                    message.id.ToString(),
                    message.message!,
                    DateTimeOffset.FromUnixTimeSeconds(message.date).UtcDateTime,
                    $"https://t.me/{identifier[1..]}/{message.id}",
                    null))
                .ToList();
            return new TelegramCollectionResult(true, false, messages, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram collection failed for {Identifier}", identifier);
            return new TelegramCollectionResult(false, false, Array.Empty<TelegramCollectedMessage>(), exception.Message);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (client is not null) return;
        await loginGate.WaitAsync(cancellationToken);
        try
        {
            if (client is null)
            {
                client = new Client(GetConfigurationValue);
                await client.LoginUserIfNeeded();
            }
        }
        finally
        {
            loginGate.Release();
        }
    }

    private int GetHistoryLimit()
    {
        var configured = configuration.GetValue<int?>("Telegram:HistoryPerChannel") ?? 500;
        return Math.Clamp(configured, 1, 2000);
    }

    private string? GetConfigurationValue(string key)
    {
        return key switch
        {
            "api_id" => configuration["Telegram:ApiId"],
            "api_hash" => configuration["Telegram:ApiHash"],
            "phone_number" => configuration["Telegram:PhoneNumber"],
            "session_pathname" => configuration["Telegram:SessionPath"] ?? "telegram.session",
            _ => null
        };
    }

    private static TelegramCollectionResult AccessRequired(string message)
    {
        return new TelegramCollectionResult(false, true, Array.Empty<TelegramCollectedMessage>(), message);
    }

    public async ValueTask DisposeAsync()
    {
        loginGate.Dispose();
        if (client is not null) await client.DisposeAsync();
    }
}
