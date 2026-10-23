using System.Diagnostics;
using System.Text.RegularExpressions;
using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.Telegram;

namespace CarShow.ApplicationService.Services;

// Read-only diagnostic collection: does not enqueue jobs or change live market prices.
public sealed class TelegramPreviewService(ITelegramCollector collector, ITelegramListingExtractor extractor)
{
    public async Task<IReadOnlyList<TelegramPreviewReport>> CollectAsync(TelegramPreviewRequest request, CancellationToken token)
    {
        var reports = new List<TelegramPreviewReport>();
        foreach (var identifier in request.Channels.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var watch = Stopwatch.StartNew();
            var username = identifier?.Trim().TrimStart('@') ?? "";
            if (!Regex.IsMatch(username, "^[A-Za-z][A-Za-z0-9_]{4,31}$"))
            {
                reports.Add(new(identifier ?? "", request.MessageLimit, 0, 0, 0, 0, null, "InvalidInput", 0, false, "شناسه کانال عمومی نامعتبر است.", []));
                continue;
            }
            var result = await collector.CollectChannelAsync(username, request.MessageLimit, request.Before, token);
            var channel = new TelegramChannel { NormalizedIdentifier = username.ToLowerInvariant() };
            var listings = result.Messages.Select(message => extractor.Extract(channel, new TelegramRawMessage
            {
                ExternalMessageId = message.ExternalMessageId, MessageText = message.Text,
                MessageDate = message.MessageDate, SourceUrl = message.SourceUrl, CollectedAt = DateTime.UtcNow
            })).Where(x => x is not null).Select(x => new TelegramPreviewListing("@" + username, x!.SourceMessageId,
                x.Model, x.Trim, x.Year, x.Mileage, x.Price, x.Currency switch { "IRT" => "Toman", "IRR" => "Rial", _ => null }, x.SourceUrl)).ToList();
            reports.Add(new("@" + username, request.MessageLimit, result.Messages.Count,
                listings.Count(x => !string.IsNullOrWhiteSpace(x.CarName)), listings.Count,
                result.PagesFetched, result.NextBefore, result.StopReason, watch.ElapsedMilliseconds,
                result.Success, result.Error, listings));
        }
        return reports;
    }
}
