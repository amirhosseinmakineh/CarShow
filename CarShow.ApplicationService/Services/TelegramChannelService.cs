using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.IRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace CarShow.ApplicationService.Services;

public sealed class TelegramChannelService(
    IBaseRepository<long, TelegramChannel> channelRepository,
    IBaseRepository<long, TelegramCollectionJob> jobRepository,
    ITelegramCollectionQueue queue,
    IConfiguration configuration) : ITelegramChannelService
{
    private static readonly Regex Username = new("^[A-Za-z][A-Za-z0-9_]{4,31}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<TelegramChannelDto>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        return await channelRepository.GetAll()
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(ToDto)
            .ToListAsync(cancellationToken);
    }

    public async Task<TelegramChannelSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAll().AsNoTracking().ToListAsync(cancellationToken);
        return new TelegramChannelSummaryDto(
            channels.Count,
            channels.Count(x => x.Status == TelegramChannelStatus.Active),
            channels.Count(x => x.Status == TelegramChannelStatus.Paused),
            channels.Count(x => x.Status is TelegramChannelStatus.Error
                or TelegramChannelStatus.AccessRequired
                or TelegramChannelStatus.UnsupportedByCurrentCredentials),
            channels.Count(x => x.Status == TelegramChannelStatus.Pending),
            channels.Count(x => x.Status == TelegramChannelStatus.Processing),
            channels.Sum(x => x.MessagesCollected),
            channels.Sum(x => x.ListingsExtracted),
            channels.Count == 0 ? null : channels.Max(x => x.LastSuccessAt));
    }

    public async Task<BulkTelegramChannelImportResult> ImportAsync(BulkTelegramChannelImportRequest request, CancellationToken cancellationToken)
    {
        var entries = (request.Input ?? string.Empty).Split(
            ['\r', '\n', ',', ';', '\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();
        var normalized = new List<(string id, string username, string url)>();
        foreach (var entry in entries)
        {
            var value = Normalize(entry);
            if (value is null)
            {
                invalid.Add(entry);
                continue;
            }
            if (unique.Add(value.Value.id))
            {
                normalized.Add(value.Value);
            }
        }
        var ids = normalized.Select(x => x.id).ToList();
        var existing = await channelRepository.GetAll()
            .Where(x => ids.Contains(x.NormalizedIdentifier))
            .Select(x => x.NormalizedIdentifier)
            .ToHashSetAsync(cancellationToken);
        var created = 0;
        var queued = 0;
        foreach (var item in normalized.Where(x => !existing.Contains(x.id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = new TelegramChannel
            {
                TelegramIdentifier = item.id,
                NormalizedIdentifier = item.id,
                Username = item.username,
                PublicUrl = string.IsNullOrEmpty(item.url) ? null : item.url,
                Status = TelegramChannelStatus.Pending
            };
            await channelRepository.Create(channel);
            await channelRepository.SaveChanges();
            await queue.EnqueueAsync(channel.Id, cancellationToken);
            created++;
            queued++;
        }
        return new(entries.Length, created, entries.Length - invalid.Count - created, invalid.Count, queued, invalid);
    }

    public async Task<TelegramChannelDto?> AddAsync(AddTelegramChannelRequest request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request.Input);
        if (normalized is null)
        {
            return null;
        }

        var result = await ImportAsync(new BulkTelegramChannelImportRequest(request.Input), cancellationToken);
        if (result.Created == 0)
        {
            return null;
        }
        var identifier = normalized.Value.id;
        return await channelRepository.GetAll()
            .Where(x => x.NormalizedIdentifier == identifier)
            .Select(ToDto)
            .SingleAsync(cancellationToken);
    }

    public async Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetAll()
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (channel is null)
        {
            return false;
        }
        channel.Enabled = enabled;
        channel.Status = enabled ? TelegramChannelStatus.Pending : TelegramChannelStatus.Paused;
        channel.UpdatedAt = DateTime.UtcNow;
        if (enabled)
        {
            channel.RetryCount = 0;
            channel.LastError = null;
        }
        await channelRepository.SaveChanges();
        if (enabled)
        {
            await queue.EnqueueAsync(id, cancellationToken);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetById(id);
        if (channel is null)
        {
            return false;
        }

        await channelRepository.Delete(channel);
        await channelRepository.SaveChanges();
        return true;
    }

    public async Task<bool> RetryAsync(long id, CancellationToken cancellationToken)
    {
        return await SetEnabledAsync(id, true, cancellationToken);
    }
    public async Task<bool> TestAsync(long id, CancellationToken cancellationToken)
    {
        return await SetEnabledAsync(id, true, cancellationToken);
    }

    public Task<TelegramCapabilityDto> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var provider = configuration["Telegram:Provider"]?.ToLowerInvariant() ?? "auto";
        var hasApiCredentials = long.TryParse(configuration["Telegram:ApiId"], out _)
            && !string.IsNullOrWhiteSpace(configuration["Telegram:ApiHash"]);

        var effectiveProvider = provider == "auto"
            ? hasApiCredentials ? "mtproto" : "web-preview"
            : provider;
        var message = effectiveProvider == "web-preview"
            ? "دریافت پیام‌های قابل مشاهده در پیش‌نمایش عمومی، بدون Bot Admin و ApiId/ApiHash؛ دسترسی کامل به تاریخچه تضمین نمی‌شود."
            : "دریافت با MTProto به ApiId/ApiHash و Session معتبر نیاز دارد؛ اتصال هنگام اجرای Job بررسی می‌شود.";
        var result = new TelegramCapabilityDto(
            effectiveProvider,
            effectiveProvider == "web-preview",
            effectiveProvider == "mtproto",
            true,
            effectiveProvider == "mtproto",
            message);
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<TelegramJobLogDto>> GetJobsAsync(CancellationToken cancellationToken)
    {
        return await jobRepository.GetAll().AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .Select(x => new TelegramJobLogDto(x.Id, x.ChannelId, x.Status.ToString(), x.Attempt, x.CreatedAt, x.CompletedAt, x.Error))
            .ToListAsync(cancellationToken);
    }

    private static (string id, string username, string url)? Normalize(string input)
    {
        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Host != "t.me" || (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                return null;
            }

            value = uri.AbsolutePath.Trim('/');
        }
        value = value.TrimStart('@');
        if (value.StartsWith("-100") && value.Length > 5 && value[1..].All(char.IsDigit))
        {
            return (value, string.Empty, string.Empty);
        }
        if (!Username.IsMatch(value))
        {
            return null;
        }
        return (value.ToLowerInvariant(), value, $"https://t.me/{value.ToLowerInvariant()}");
    }

    private static readonly System.Linq.Expressions.Expression<Func<TelegramChannel, TelegramChannelDto>> ToDto = x => new(
        x.Id,
        x.TelegramIdentifier,
        x.Username,
        x.PublicUrl,
        x.Title,
        x.Status.ToString(),
        x.Enabled,
        x.LastAttemptAt,
        x.LastSuccessAt,
        x.LastError,
        x.MessagesCollected,
        x.ListingsExtracted,
        x.RetryCount);
}
