using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TL;
using WTelegram;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramCollector(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<TelegramCollector> logger) : ITelegramCollector, IAsyncDisposable
{
    private readonly SemaphoreSlim loginGate = new(1, 1);
    private Client? client;

    public async Task<TelegramCollectionResult> CollectChannelAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var provider = configuration["Telegram:Provider"]?.ToLowerInvariant() ?? "auto";
        var hasMtProtoCredentials = long.TryParse(configuration["Telegram:ApiId"], out _)
            && !string.IsNullOrWhiteSpace(configuration["Telegram:ApiHash"]);

        if (provider == "web-preview" || (provider == "auto" && !hasMtProtoCredentials))
        {
            return await CollectFromWebPreviewAsync(identifier, cancellationToken);
        }

        return await CollectWithMtProtoAsync(identifier, cancellationToken);
    }

    private async Task<TelegramCollectionResult> CollectWithMtProtoAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(configuration["Telegram:ApiId"], out _)
            || string.IsNullOrWhiteSpace(configuration["Telegram:ApiHash"]))
        {
            return AccessRequired("Telegram ApiId/ApiHash are not configured.");
        }

        if (!identifier.StartsWith("@", StringComparison.Ordinal))
        {
            identifier = $"@{identifier}";
        }

        if (identifier.StartsWith("@-100", StringComparison.Ordinal))
        {
            return AccessRequired("Numeric identifiers require an MTProto peer with a resolved access hash.");
        }

        try
        {
            await EnsureConnectedAsync(cancellationToken);
            var peer = await client!.Contacts_ResolveUsername(identifier[1..]).WaitAsync(cancellationToken);
            var historyLimit = GetHistoryLimit();
            var history = await client.Messages_GetHistory(peer, limit: historyLimit).WaitAsync(cancellationToken);
            var messages = history.Messages
                .OfType<Message>()
                .Where(message => !string.IsNullOrWhiteSpace(message.message))
                .Select(message => new TelegramCollectedMessage(
                    message.id.ToString(),
                    message.message!,
                    message.date.ToUniversalTime(),
                    $"https://t.me/{identifier[1..]}/{message.id}",
                    null))
                .ToList();

            if (messages.Count == 0)
            {
                return new TelegramCollectionResult(
                    false,
                    false,
                    messages,
                    "Telegram returned no readable text messages for this channel.");
            }

            return new TelegramCollectionResult(true, false, messages, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "MTProto collection failed for {Identifier}", identifier);
            return new TelegramCollectionResult(false, false, Array.Empty<TelegramCollectedMessage>(), exception.Message);
        }
    }

    private async Task<TelegramCollectionResult> CollectFromWebPreviewAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var username = identifier.Trim().TrimStart('@');
        if (username.StartsWith("-100", StringComparison.Ordinal)
            || username.Contains('/'))
        {
            return AccessRequired("Web Preview supports public channel usernames only.");
        }

        var url = $"https://t.me/s/{Uri.EscapeDataString(username)}";
        using var client = httpClientFactory.CreateClient("TelegramWebPreview");

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TelegramCollectionResult(
                    false,
                    response.StatusCode is System.Net.HttpStatusCode.Forbidden
                        or System.Net.HttpStatusCode.NotFound,
                    Array.Empty<TelegramCollectedMessage>(),
                    $"Telegram web preview returned {(int)response.StatusCode}.");
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);
            if (document.DocumentNode.SelectSingleNode("//*[contains(@class,'tgme_channel_history')]") is null)
            {
                return AccessRequired("This channel has no accessible public web preview.");
            }

            var messages = document.DocumentNode
                .SelectNodes("//div[contains(@class,'tgme_widget_message_wrap')]")?
                .Select(node => ParsePreviewMessage(node, username))
                .Where(message => message is not null)
                .Cast<TelegramCollectedMessage>()
                .ToList() ?? [];

            if (messages.Count == 0)
            {
                return new TelegramCollectionResult(
                    false,
                    false,
                    messages,
                    "Telegram returned no readable text messages for this channel.");
            }

            return new TelegramCollectionResult(true, false, messages, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram web preview collection failed for {Username}", username);
            return new TelegramCollectionResult(false, false, Array.Empty<TelegramCollectedMessage>(), exception.Message);
        }
    }

    private static TelegramCollectedMessage? ParsePreviewMessage(HtmlNode node, string username)
    {
        var post = node.SelectSingleNode(".//*[@data-post]")?.GetAttributeValue("data-post", string.Empty)
            ?? node.GetAttributeValue("data-post", string.Empty);
        var messageId = post[(post.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var textNode = node.SelectSingleNode(".//div[contains(@class,'tgme_widget_message_text')]");
        if (textNode is not null)
        {
            foreach (var lineBreak in textNode.SelectNodes(".//br") ?? Enumerable.Empty<HtmlNode>())
            {
                lineBreak.ParentNode.ReplaceChild(HtmlNode.CreateNode("&#10;"), lineBreak);
            }
        }

        var text = HtmlEntity.DeEntitize(textNode?.InnerText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var timeNode = node.SelectSingleNode(".//time[@datetime]");
        var date = DateTime.TryParse(
            timeNode?.GetAttributeValue("datetime", string.Empty),
            out var parsedDate)
            ? parsedDate.ToUniversalTime()
            : DateTime.UtcNow;

        return new TelegramCollectedMessage(
            messageId,
            text,
            date,
            $"https://t.me/{username}/{messageId}",
            null);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            return;
        }
        await loginGate.WaitAsync(cancellationToken);
        try
        {
            if (client is null)
            {
                client = new Client(GetConfigurationValue);
                await client.LoginUserIfNeeded().WaitAsync(cancellationToken);
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
        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }
}
