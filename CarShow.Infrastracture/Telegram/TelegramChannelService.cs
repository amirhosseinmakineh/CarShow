using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramChannelService(CarShowContext db, ITelegramCollectionQueue queue) : ITelegramChannelService
{
    private static readonly Regex Username = new("^[A-Za-z][A-Za-z0-9_]{4,31}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<TelegramChannelDto>> GetChannelsAsync(CancellationToken cancellationToken) =>
        await db.TelegramChannels.AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(ToDto).ToListAsync(cancellationToken);

    public async Task<TelegramChannelSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var channels = await db.TelegramChannels.AsNoTracking().ToListAsync(cancellationToken);
        return new(channels.Count, channels.Count(x => x.Status == TelegramChannelStatus.Active), channels.Count(x => x.Status == TelegramChannelStatus.Paused), channels.Count(x => x.Status is TelegramChannelStatus.Error or TelegramChannelStatus.AccessRequired), channels.Count(x => x.Status == TelegramChannelStatus.Pending), channels.Count(x => x.Status == TelegramChannelStatus.Processing), channels.Sum(x => x.MessagesCollected), channels.Sum(x => x.ListingsExtracted), channels.Max(x => x.LastSuccessAt));
    }

    public async Task<BulkTelegramChannelImportResult> ImportAsync(BulkTelegramChannelImportRequest request, CancellationToken cancellationToken)
    {
        var entries = (request.Input ?? string.Empty).Split(new[] { '\r', '\n', ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();
        var normalized = new List<(string id, string username, string url)>();
        foreach (var entry in entries)
        {
            var value = Normalize(entry);
            if (value is null) { invalid.Add(entry); continue; }
            if (unique.Add(value.Value.id)) normalized.Add(value.Value);
        }
        var ids = normalized.Select(x => x.id).ToList();
        var existing = await db.TelegramChannels.Where(x => ids.Contains(x.NormalizedIdentifier)).Select(x => x.NormalizedIdentifier).ToHashSetAsync(cancellationToken);
        var created = 0;
        var queued = 0;
        foreach (var item in normalized.Where(x => !existing.Contains(x.id)))
        {
            var channel = new TelegramChannel { TelegramIdentifier = item.id, NormalizedIdentifier = item.id, Username = item.username, PublicUrl = string.IsNullOrEmpty(item.url) ? null : item.url, Status = TelegramChannelStatus.Pending };
            db.TelegramChannels.Add(channel);
            await db.SaveChangesAsync(cancellationToken);
            await queue.EnqueueAsync(channel.Id, cancellationToken);
            created++; queued++;
        }
        return new(entries.Length, created, entries.Length - invalid.Count - normalized.Count, invalid.Count, queued, invalid);
    }

    public async Task<TelegramChannelDto?> AddAsync(AddTelegramChannelRequest request, CancellationToken cancellationToken)
    {
        var result = await ImportAsync(new BulkTelegramChannelImportRequest(request.Input), cancellationToken);
        if (result.Created == 0) return null;
        return await db.TelegramChannels.OrderByDescending(x => x.Id).Select(ToDto).FirstAsync(cancellationToken);
    }

    public async Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken)
    {
        var channel = await db.TelegramChannels.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (channel is null) return false;
        channel.Enabled = enabled; channel.Status = enabled ? TelegramChannelStatus.Pending : TelegramChannelStatus.Paused; channel.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (enabled) await queue.EnqueueAsync(id, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken) { var channel = await db.TelegramChannels.FindAsync(new object[] { id }, cancellationToken); if (channel is null) return false; db.TelegramChannels.Remove(channel); await db.SaveChangesAsync(cancellationToken); return true; }
    public async Task<bool> RetryAsync(long id, CancellationToken cancellationToken) { var ok = await SetEnabledAsync(id, true, cancellationToken); return ok; }
    public async Task<bool> TestAsync(long id, CancellationToken cancellationToken) { var channel = await db.TelegramChannels.FindAsync(new object[] { id }, cancellationToken); if (channel is null) return false; await queue.EnqueueAsync(id, cancellationToken); return true; }

    private static (string id, string username, string url)? Normalize(string input)
    {
        var value = input.Trim();
        if (value.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://t.me/", StringComparison.OrdinalIgnoreCase)) value = value[(value.IndexOf("t.me/", StringComparison.OrdinalIgnoreCase) + 5)..].Trim('/');
        value = value.TrimStart('@');
        if (value.StartsWith("-100") && value.Length > 5 && value.All(char.IsDigit)) return (value, string.Empty, string.Empty);
        if (!Username.IsMatch(value)) return null;
        return (value.ToLowerInvariant(), value, $"https://t.me/{value}");
    }

    private static readonly System.Linq.Expressions.Expression<Func<TelegramChannel, TelegramChannelDto>> ToDto = x => new(x.Id, x.TelegramIdentifier, x.Username, x.PublicUrl, x.Title, x.Status.ToString(), x.Enabled, x.LastAttemptAt, x.LastSuccessAt, x.LastError, x.MessagesCollected, x.ListingsExtracted, x.RetryCount);
}
