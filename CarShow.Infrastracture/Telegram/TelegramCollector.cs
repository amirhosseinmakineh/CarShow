using System.Globalization;
using System.Text.RegularExpressions;
using CarShow.Domain.Telegram;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CarShow.Infrastracture.Telegram;

// Only public HTTPS previews are used. No Telegram account, session, or API credentials.
public sealed class TelegramCollector(IConfiguration configuration, IHttpClientFactory httpClientFactory,
    ILogger<TelegramCollector> logger) : ITelegramCollector
{
    public Task<TelegramCollectionResult> CollectChannelAsync(string identifier, CancellationToken cancellationToken)
        => CollectChannelAsync(identifier, Math.Clamp(configuration.GetValue("Telegram:HistoryLimit", 500), 1, 500), null, cancellationToken);

    public async Task<TelegramCollectionResult> CollectChannelAsync(string identifier, int messageLimit, long? before,
        CancellationToken cancellationToken)
    {
        var username = identifier.Trim().TrimStart('@');
        var limit = Math.Clamp(messageLimit, 1, 500);
        if (!Regex.IsMatch(username, "^[A-Za-z][A-Za-z0-9_]{4,31}$") || before is <= 0)
            return new(false, true, [], "Only public channel usernames and a positive before cursor are supported.", limit, 0, "InvalidInput");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Telegram:ChannelTimeoutSeconds", 180), 10, 300)));
        var token = timeout.Token;
        using var client = httpClientFactory.CreateClient("TelegramWebPreview");
        var messages = new Dictionary<long, TelegramCollectedMessage>();
        var pages = 0;
        var cursor = before;
        var reason = "PreviewExhausted";
        string? error = null;
        var accessRequired = false;
        try
        {
            // Cap requests independently of readable messages (media-only pages are possible).
            for (var page = 0; page < 100 && messages.Count < limit; page++)
            {
                var url = $"https://t.me/s/{Uri.EscapeDataString(username)}" + (cursor.HasValue ? $"?before={cursor.Value}" : "");
                using var response = await client.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                {
                    error = $"Telegram web preview returned HTTP {(int)response.StatusCode}.";
                    accessRequired = response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound;
                    reason = "HttpError";
                    break;
                }
                pages++;
                var document = new HtmlDocument();
                document.LoadHtml(await response.Content.ReadAsStringAsync(token));
                if (document.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' tgme_channel_history ')]") is null)
                {
                    error = "This channel has no accessible public web preview.";
                    accessRequired = true;
                    reason = "PreviewUnavailable";
                    break;
                }
                var nodes = document.DocumentNode.SelectNodes("//*[@data-post]")?.AsEnumerable() ?? Enumerable.Empty<HtmlNode>();
                var parsed = nodes.Select(n => (Node: n, Id: ParseId(n))).Where(x => x.Id > 0 && (!cursor.HasValue || x.Id < cursor)).ToList();
                if (parsed.Count == 0) { reason = "PreviewExhausted"; break; }
                var oldest = parsed.Min(x => x.Id);
                foreach (var item in parsed.OrderByDescending(x => x.Id))
                {
                    var message = ParseMessage(item.Node, username, item.Id);
                    if (message is not null) messages.TryAdd(item.Id, message);
                    if (messages.Count >= limit) break;
                }
                // Continue below the last returned message so a client never skips the rest of a page.
                if (messages.Count >= limit)
                {
                    cursor = messages.Keys.Min();
                    reason = "RequestedLimitReached";
                    break;
                }
                if (cursor.HasValue && oldest >= cursor) { reason = "NoProgress"; break; }
                cursor = oldest;
                reason = page == 99 ? "PageLimitReached" : "PreviewExhausted";
                await Task.Delay(Math.Clamp(configuration.GetValue("Telegram:RequestDelayMilliseconds", 350), 0, 5000), token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            error = "Telegram channel request timed out; partial messages are included.";
            reason = "Timeout";
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Telegram preview request failed for {Username}", username);
            error = "Telegram connection failed. Check backend logs for network details.";
            reason = "NetworkError";
        }
        logger.LogInformation("Telegram {Username}: requested={Requested}, received={Received}, pages={Pages}, stop={Reason}", username, limit, messages.Count, pages, reason);
        return new(error is null && messages.Count > 0, accessRequired,
            messages.OrderByDescending(x => x.Key).Select(x => x.Value).ToList(), error, limit, pages, reason, cursor);
    }

    private static long ParseId(HtmlNode node)
    {
        var post = node.GetAttributeValue("data-post", "");
        return long.TryParse(post[(post.LastIndexOf('/') + 1)..], out var id) ? id : 0;
    }

    private static TelegramCollectedMessage? ParseMessage(HtmlNode node, string username, long id)
    {
        var textNode = node.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' tgme_widget_message_text ')]");
        var mediaUrls = node.SelectNodes(".//*[contains(@class,'tgme_widget_message_photo_wrap') or contains(@class,'tgme_widget_message_video_player')]")?
            .Select(x => x.GetAttributeValue("style", ""))
            .SelectMany(x => Regex.Matches(x, "url\\([\'\"]?(?<url>[^\'\")]+)").Select(m => m.Groups["url"].Value))
            .Concat(node.SelectNodes(".//img[@src]")?.Select(x => x.GetAttributeValue("src", "")) ?? [])
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (textNode is null && mediaUrls.Count == 0) return null;
        foreach (var br in textNode?.SelectNodes(".//br")?.ToList() ?? [])
            br.ParentNode.ReplaceChild(HtmlNode.CreateNode("&#10;"), br);
        var text = textNode is null ? string.Empty : HtmlEntity.DeEntitize(textNode.InnerText).Trim();
        var time = node.SelectSingleNode(".//time[@datetime]")?.GetAttributeValue("datetime", "");
        if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return null;
        return new(id.ToString(CultureInfo.InvariantCulture), text, date.UtcDateTime, $"https://t.me/{username}/{id}", string.Join("\n", mediaUrls));
    }
}
