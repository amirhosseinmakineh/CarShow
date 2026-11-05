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
        var channels = await channelRepository.GetAll().AsNoTracking().Where(x => !x.IsDelete)
            .OrderByDescending(x => x.CreatedAt).Select(ToDto).ToListAsync(cancellationToken);
        var stats = await processingStore.Listings.AsNoTracking().Where(x => !x.IsDelete
            && processingStore.Channels.Any(channel => channel.Id == x.ChannelId && !channel.IsDelete))
            .GroupBy(x => x.ChannelId).Select(x => new
            {
                x.Key,
                Candidates = x.Count(y => y.MatchStatus != "Ignored"),
                Processed = x.Count(y => y.ProcessingStatus == "Published"),
                Failed = x.Count(y => y.ProcessingStatus == "Rejected" || y.ProcessingStatus == "Failed"),
                Review = x.Count(y => y.ProcessingStatus == "NeedsReview"),
                Waiting = x.Count(y => y.ProcessingStatus == "WaitingForComparableChannels"),
                Priced = x.Count(y => y.NormalizedPrice > 0 && y.MatchStatus != "Ignored"),
                Named = x.Count(y => y.NameValidationStatus == "Validated"),
                Compared = x.Count(y => y.ComparisonStatus == "Compared"),
                References = x.Where(y => y.ProcessingStatus == "Published" && y.CarId != null && y.PriceDay != null)
                    .Select(y => new { y.CarId, y.PriceDay }).Distinct().Count(),
                CalculatedAt = x.Max(y => y.ComparedAt)
            }).ToListAsync(cancellationToken);
        var ignored = await processingStore.Messages.AsNoTracking()
            .Where(x => !x.IsDelete && !x.Channel.IsDelete && x.ProcessingStatus == "Ignored")
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
                ValidNameListings = stat?.Named ?? 0,
                ComparedListings = stat?.Compared ?? 0,
                ReferencePriceGroups = stat?.References ?? 0,
                LastCalculationAt = stat?.CalculatedAt,
                IgnoredMessages = ignored.FirstOrDefault(x => x.Key == channel.Id)?.Count ?? 0
            };
        }).ToList();
    }

    public async Task<TelegramChannelSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAll().AsNoTracking().Where(x => !x.IsDelete)
            .ToListAsync(cancellationToken);
        var activeChannelIds = processingStore.Channels.AsNoTracking()
            .Where(channel => !channel.IsDelete).Select(channel => channel.Id);
        var listings = processingStore.Listings.AsNoTracking().Where(x => !x.IsDelete
            && x.MatchStatus != "Ignored" && activeChannelIds.Contains(x.ChannelId));
        return new TelegramChannelSummaryDto(
            channels.Count,
            channels.Count(x => x.Status == TelegramChannelStatus.Active),
            channels.Count(x => x.Status == TelegramChannelStatus.Paused),
            channels.Count(x => x.Status is TelegramChannelStatus.Error or TelegramChannelStatus.AccessRequired or TelegramChannelStatus.UnsupportedByCurrentCredentials),
            channels.Count(x => x.Status == TelegramChannelStatus.Pending), channels.Count(x => x.Status == TelegramChannelStatus.Processing),
            await processingStore.Messages.CountAsync(x => !x.IsDelete && activeChannelIds.Contains(x.ChannelId), cancellationToken), await listings.CountAsync(cancellationToken), channels.Count == 0 ? null : channels.Max(x => x.LastSuccessAt),
            await listings.Where(x => x.IsUsed == false && x.MatchStatus == "Matched" && x.Car != null && !x.Car.IsDelete).Select(x => x.CarId).Distinct().CountAsync(cancellationToken),
            await listings.Where(x => x.IsUsed == true && x.MatchStatus == "Matched" && x.Car != null && !x.Car.IsDelete).Select(x => x.CarId).Distinct().CountAsync(cancellationToken),
            await listings.Where(x => x.ComparisonStatus == "Compared" && x.ValidPriceSourceCount > 0 && x.Car != null && !x.Car.IsDelete).Select(x => x.CarId).Distinct().CountAsync(cancellationToken),
            await listings.CountAsync(x => x.ProcessingStatus == "Pending" || x.ProcessingStatus == "Processing" || x.ProcessingStatus == "NeedsReview" || x.ProcessingStatus == "WaitingForComparableChannels", cancellationToken),
            await listings.CountAsync(x => x.ProcessingStatus == "Rejected" || x.ProcessingStatus == "Failed", cancellationToken), await listings.CountAsync(x => x.PrimaryImageUrl == null, cancellationToken),
            await listings.CountAsync(x => x.Manufacturer == null || x.Manufacturer == "", cancellationToken),
            await processingStore.Messages.CountAsync(x => !x.IsDelete && x.ProcessingStatus == "Ignored" && activeChannelIds.Contains(x.ChannelId), cancellationToken),
            await listings.CountAsync(x => x.NormalizedPrice > 0, cancellationToken),
            await processingStore.Listings.CountAsync(x => !x.IsDelete && x.MatchStatus != "Ignored", cancellationToken));
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
        var existing = await channelRepository.GetAll().AsTracking()
            .Where(x => ids.Contains(x.NormalizedIdentifier))
            .ToDictionaryAsync(x => x.NormalizedIdentifier, cancellationToken);
        var created = 0;
        var queued = 0;
        foreach (var item in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existing.TryGetValue(item.id, out var saved))
            {
                if (!saved.IsDelete)
                {
                    continue;
                }

                saved.IsDelete = false;
                saved.Enabled = true;
                saved.Status = item.id.StartsWith("+", StringComparison.Ordinal)
                    ? TelegramChannelStatus.AccessRequired : TelegramChannelStatus.Pending;
                saved.RetryCount = 0;
                saved.LastError = item.id.StartsWith("+", StringComparison.Ordinal)
                    ? "لینک دعوت خصوصی است؛ دریافت عمومی ممکن نیست." : null;
                saved.UpdatedAt = DateTime.UtcNow;
                await channelRepository.SaveChanges();
                created++;
                try { if (saved.Status != TelegramChannelStatus.AccessRequired) { await queue.EnqueueAsync(saved.Id, cancellationToken); queued++; } }
                catch (CarShow.Domain.Telegram.TelegramProcessingBusyException) { /* Pending is durable; backend scheduler retries. */ }
                continue;
            }

            var channel = new TelegramChannel
            {
                TelegramIdentifier = item.id,
                NormalizedIdentifier = item.id,
                Username = item.username,
                PublicUrl = string.IsNullOrEmpty(item.url) ? null : item.url,
                Status = item.id.StartsWith("+", StringComparison.Ordinal) ? TelegramChannelStatus.AccessRequired : TelegramChannelStatus.Pending,
                LastError = item.id.StartsWith("+", StringComparison.Ordinal) ? "لینک دعوت خصوصی است؛ دریافت عمومی ممکن نیست." : null
            };
            await channelRepository.Create(channel);
            await channelRepository.SaveChanges();
            created++;
            try { if (channel.Status != TelegramChannelStatus.AccessRequired) { await queue.EnqueueAsync(channel.Id, cancellationToken); queued++; } }
            catch (CarShow.Domain.Telegram.TelegramProcessingBusyException) { /* Do not abort registration of other sources. */ }
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
            .Where(x => x.NormalizedIdentifier == identifier && !x.IsDelete)
            .Select(ToDto)
            .SingleAsync(cancellationToken);
    }

    public async Task<TelegramChannelDto?> UpdateAsync(long id, UpdateTelegramChannelRequest request, CancellationToken ct)
    {
        var normalized = Normalize(request.Input);
        if (normalized is null) return null;
        var current = await channelRepository.GetAll().AsTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDelete, ct);
        if (current is null) return null;
        // Changing the Telegram identity creates/reuses a different source row. Old message IDs
        // remain attached to their original channel; they cannot collide with the new channel.
        if (current.NormalizedIdentifier != normalized.Value.id)
        {
            await SetEnabledAsync(id, false, ct);
            await ImportAsync(new(request.Input), ct);
            current = await channelRepository.GetAll().AsTracking().SingleAsync(x => x.NormalizedIdentifier == normalized.Value.id, ct);
        }
        current.Title = request.Title.Trim(); current.UpdatedAt = DateTime.UtcNow;
        await channelRepository.SaveChanges();
        return await channelRepository.GetAll().Where(x => x.Id == current.Id).Select(ToDto).SingleAsync(ct);
    }

    public async Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetAll()
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (channel is null || channel.IsDelete)
        {
            return false;
        }
        if (enabled && channel.LastError?.StartsWith("DuplicateSourceAlias:", StringComparison.Ordinal) == true)
            throw new ArgumentException("این رکورد تکراری است؛ منبع اصلی مشخص‌شده در آخرین خطا را مدیریت کنید.");
        channel.Enabled = enabled;
        var privateInvite = channel.NormalizedIdentifier.StartsWith("+", StringComparison.Ordinal);
        channel.Status = enabled ? privateInvite ? TelegramChannelStatus.AccessRequired : TelegramChannelStatus.Pending : TelegramChannelStatus.Paused;
        channel.UpdatedAt = DateTime.UtcNow;
        if (enabled)
        {
            channel.RetryCount = 0;
            channel.LastError = privateInvite ? "لینک دعوت خصوصی است؛ دریافت عمومی ممکن نیست." : null;
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
        if (enabled && !privateInvite)
        {
            try { await queue.EnqueueAsync(id, cancellationToken); }
            catch (CarShow.Domain.Telegram.TelegramProcessingBusyException) { /* Enabled/Pending survives until the scheduler retries. */ }
        }
        return true;
    }

    public async Task<int> SetEnabledBulkAsync(BulkTelegramChannelEnabledRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ChannelIds?.Distinct().ToArray() ?? [];
        if (ids.Length == 0 || ids.Length > 500 || ids.Any(id => id <= 0))
            throw new ArgumentException("بین ۱ تا ۵۰۰ شناسه معتبر انتخاب کنید.");

        var found = await channelRepository.GetAll().AsNoTracking()
            .CountAsync(channel => ids.Contains(channel.Id) && !channel.IsDelete, cancellationToken);
        if (found != ids.Length)
            throw new ArgumentException("یک یا چند کانال انتخاب‌شده پیدا نشد؛ فهرست را به‌روز کنید.");

        var updated = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await SetEnabledAsync(id, request.Enabled, cancellationToken)) updated++;
        }
        return updated;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetById(id);
        if (channel is null || channel.IsDelete)
        {
            return false;
        }

        channel.IsDelete = true;
        channel.Enabled = false;
        channel.Status = TelegramChannelStatus.Paused;
        channel.UpdatedAt = DateTime.UtcNow;
        var queuedJobs = await jobRepository.GetAll().AsTracking()
            .Where(job => job.ChannelId == id && job.Status == TelegramCollectionJobStatus.Queued)
            .ToListAsync(cancellationToken);
        foreach (var job in queuedJobs)
        {
            job.Status = TelegramCollectionJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.Error = "Channel removed before collection started.";
        }
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

    public async Task<IReadOnlyList<TelegramJobLogDto>> GetJobsAsync(CancellationToken cancellationToken, int page = 0)
    {
        return await jobRepository.GetAll().AsNoTracking().Where(x => !x.Channel.IsDelete)
            .OrderByDescending(x => x.CreatedAt)
            .Skip(Math.Clamp(page, 0, 100000) * 50).Take(50)
            .Select(x => new TelegramJobLogDto(x.Id, x.ChannelId, x.Status.ToString(), x.Attempt, x.CreatedAt, x.CompletedAt, x.Error, x.StartedAt, x.NewRawMessages, x.ExtractedListings,
                x.AcceptedListings, x.RejectedListings, x.CalculatedPriceGroups, x.PricingCompletedAt, x.RejectionReasons, x.StageErrors, x.ReportVersion))
            .ToListAsync(cancellationToken);
    }

    public static (string id, string username, string url)? Normalize(string input)
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
        if (value.StartsWith("s/", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        if (value.StartsWith("+", StringComparison.Ordinal) && Regex.IsMatch(value[1..], "^[A-Za-z0-9_-]{8,128}$"))
            return (value, "", $"https://t.me/{value}");
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
