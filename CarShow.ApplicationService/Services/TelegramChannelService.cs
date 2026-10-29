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
    IConfiguration configuration,
    ITelegramProcessingStore processingStore) : ITelegramChannelService
{
    private static readonly Regex Username = new("^[A-Za-z][A-Za-z0-9_]{4,31}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<TelegramChannelDto>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAll().AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(ToDto).ToListAsync(cancellationToken);
        var stats = await processingStore.Listings.AsNoTracking().Where(x => !x.IsDelete)
            .GroupBy(x => x.ChannelId).Select(x => new
            {
                x.Key,
                Candidates = x.Count(y => y.MatchStatus != "Ignored"),
                Processed = x.Count(y => y.ProcessingStatus == "Published"),
                Failed = x.Count(y => y.ProcessingStatus == "Rejected" || y.ProcessingStatus == "Failed"),
                Review = x.Count(y => y.ProcessingStatus == "NeedsReview"),
                Waiting = x.Count(y => y.ProcessingStatus == "WaitingForComparableChannels"),
                Priced = x.Count(y => y.NormalizedPrice > 0 && y.MatchStatus != "Ignored")
            }).ToListAsync(cancellationToken);
        var ignored = await processingStore.Messages.AsNoTracking()
            .Where(x => !x.IsDelete && x.ProcessingStatus == "Ignored")
            .GroupBy(x => x.ChannelId).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(cancellationToken);
        return channels.Select(channel =>
        {
            var stat = stats.FirstOrDefault(x => x.Key == channel.Id);
            return channel with
            {
                ListingsExtracted = stat?.Candidates ?? 0,
                ProcessedListings = stat?.Processed ?? 0, FailedListings = stat?.Failed ?? 0,
                NeedsReviewListings = stat?.Review ?? 0, WaitingComparisonListings = stat?.Waiting ?? 0,
                ValidPriceListings = stat?.Priced ?? 0,
                IgnoredMessages = ignored.FirstOrDefault(x => x.Key == channel.Id)?.Count ?? 0
            };
        }).ToList();
    }

    public async Task<TelegramChannelSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAll().AsNoTracking().ToListAsync(cancellationToken);
        var listings = processingStore.Listings.AsNoTracking().Where(x => !x.IsDelete && x.MatchStatus != "Ignored");
        return new TelegramChannelSummaryDto(
            channels.Count,
            channels.Count(x => x.Status == TelegramChannelStatus.Active),
            channels.Count(x => x.Status == TelegramChannelStatus.Paused),
            channels.Count(x => x.Status is TelegramChannelStatus.Error or TelegramChannelStatus.AccessRequired or TelegramChannelStatus.UnsupportedByCurrentCredentials),
            channels.Count(x => x.Status == TelegramChannelStatus.Pending), channels.Count(x => x.Status == TelegramChannelStatus.Processing),
            await processingStore.Messages.CountAsync(x => !x.IsDelete, cancellationToken), await listings.CountAsync(cancellationToken), channels.Count == 0 ? null : channels.Max(x => x.LastSuccessAt),
            await listings.Where(x => x.IsUsed == false && x.MatchStatus == "Matched" && x.CarId != null).Select(x => x.CarId).Distinct().CountAsync(cancellationToken),
            await listings.Where(x => x.IsUsed == true && x.MatchStatus == "Matched" && x.CarId != null).Select(x => x.CarId).Distinct().CountAsync(cancellationToken),
            await listings.Where(x => x.ComparisonStatus == "Compared" && x.ValidPriceSourceCount > 0 && x.CarId != null).Select(x => x.CarId).Distinct().CountAsync(cancellationToken),
            await listings.CountAsync(x => x.ProcessingStatus == "Pending" || x.ProcessingStatus == "Processing" || x.ProcessingStatus == "NeedsReview" || x.ProcessingStatus == "WaitingForComparableChannels", cancellationToken),
            await listings.CountAsync(x => x.ProcessingStatus == "Rejected" || x.ProcessingStatus == "Failed", cancellationToken), await listings.CountAsync(x => x.PrimaryImageUrl == null, cancellationToken),
            await listings.CountAsync(x => (x.Manufacturer == null || x.Manufacturer == "") && x.MatchStatus != "Matched", cancellationToken),
            await processingStore.Messages.CountAsync(x => !x.IsDelete && x.ProcessingStatus == "Ignored", cancellationToken),
            await listings.CountAsync(x => x.NormalizedPrice > 0, cancellationToken));
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
        else
        {
            var queuedJobs = await jobRepository.GetAll().AsTracking()
                .Where(job => job.ChannelId == id && job.Status == TelegramCollectionJobStatus.Queued)
                .ToListAsync(cancellationToken);
            foreach (var job in queuedJobs)
            {
                job.Status = TelegramCollectionJobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.Error = "Collection paused before it started.";
            }
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
        var result = new TelegramCapabilityDto(
            "web-preview", true, false, true, false,
            "دریافت صفحه‌بندی‌شده پیام‌های عمومی بدون ApiId/ApiHash؛ حداکثر ۵۰۰ پیام در هر درخواست. دسترسی کامل به تاریخچه تضمین نمی‌شود و تعداد واقعی گزارش می‌شود.");
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
