using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace CarShow.ApplicationService.Services.ZeroVehicles;

public sealed class ZeroVehicleService(IZeroVehicleStore store, ITelegramCollector collector, IConfiguration configuration, ICarIrMarketPriceCrawler? market = null,
    ITelegramProcessingStore? sources = null, ITelegramChannelService? sourceManagement = null)
{
    private readonly ZeroVehicleOptions options = ZeroVehicleOptions.Read(configuration);
    private static readonly Expression<Func<ZeroVehicleImportRun, ZeroImportDto>> RunProjection = run => new(
        run.Id, run.Channel, run.Status, run.MessageLimit, run.Before, run.NextBefore, run.PagesFetched,
        run.StopReason, run.CreatedAt, run.CompletedAt, run.Scanned, run.Accepted, run.Rejected,
        run.Duplicates, run.Stored, run.PriceGroups, run.RejectionCounts, run.Error, run.Extracted, run.StartedAt);
    private IQueryable<ZeroListingDto> ListingProjection(IQueryable<ZeroVehicleListing> query) => query.Select(x => new ZeroListingDto(
        x.Id, x.ModelId, x.Model.Name, x.TrimId, x.Trim.Name, x.OriginalName, x.PriceToman, x.Year,
        x.PublishedAt, x.ReceivedAt, x.Channel, x.ExternalMessageId, x.SourceUrl, x.PricingStatus, x.MarketPrice, x.FinalPrice, x.MarketStatus, x.MarketName, x.MarketUrl,
        x.MarketPriceAt, x.MarketFetchedAt, x.RejectionReason, x.PartIndex, x.RawPrice, x.YearCalendar, x.PriceDay, x.RawCarName));

    public async Task<ZeroImportDto> EnqueueAsync(ZeroImportRequest request, CancellationToken ct)
    {
        var channel = NormalizeChannel(request.Channel);
        if (sources is not null && !await sources.Channels.AnyAsync(x => x.NormalizedIdentifier == channel && x.Enabled && !x.IsDelete, ct))
            throw new ArgumentException("کانال باید ابتدا در منابع مشترک ثبت و فعال شود.");
        if (request.MessageLimit is < 1 or > 500 || request.Before is <= 0) throw new ArgumentException("تعداد پیام باید بین ۱ و ۵۰۰ و نشانگر ادامه مثبت باشد.");
        ZeroVehicleImportRun? run = null;
        await store.ExclusiveAsync(async () =>
        {
            run = await store.Runs.FirstOrDefaultAsync(x => x.Channel == channel && (x.Status == "Queued" || x.Status == "Processing"), ct);
            if (run is not null) throw new InvalidOperationException("دریافت دیگری برای این کانال در حال اجراست.");
            var savedChannel = await store.Channels.SingleOrDefaultAsync(x => x.Identifier == channel, ct);
            if (savedChannel is null) { savedChannel = new() { Identifier = channel }; store.Add(savedChannel); }
            run = new() { Channel = channel, MessageLimit = request.MessageLimit, Before = request.Before ?? savedChannel.ScanBefore, CreatedAt = DateTime.UtcNow };
            store.Add(run);
        }, ct);
        return (await GetRunAsync(run!.Id, ct))!;
    }
    public static string NormalizeChannel(string channel)
    {
        channel = channel.Trim();
        channel = Regex.Replace(channel, @"^https?://(?:www\.)?(?:t\.me|telegram\.me)/(?:s/)?", "", RegexOptions.IgnoreCase).TrimEnd('/').TrimStart('@').ToLowerInvariant();
        if (!Regex.IsMatch(channel, @"^[a-z][a-z0-9_]{4,31}$")) throw new ArgumentException("شناسهٔ یک کانال عمومی تلگرام را وارد کنید.");
        return channel;
    }

    public Task<ZeroImportDto?> GetRunAsync(long id, CancellationToken ct) => store.Runs.AsNoTracking().Where(x => x.Id == id).Select(RunProjection).SingleOrDefaultAsync(ct);
    public async Task<ZeroPage<ZeroImportDto>> RunsAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination(page, pageSize);
        return new(await store.Runs.AsNoTracking().OrderByDescending(x => x.Id).Skip(page * pageSize).Take(pageSize).Select(RunProjection).ToListAsync(ct), await store.Runs.CountAsync(ct), page, pageSize);
    }

    // Claim with a database lock so multiple API instances cannot consume the same run.
    public async Task<long?> ClaimAsync(DateTime now, CancellationToken ct)
    {
        long? id = null;
        await store.ExclusiveAsync(async () =>
        {
            // A crashed worker can be resumed after its lease expires. Inserts are idempotent.
            var stale = await store.Runs.Where(x => x.Status == "Processing" && x.StartedAt < now.AddMinutes(-10)).ToListAsync(ct);
            foreach (var item in stale) item.Status = "Queued";
            await store.SaveAsync(ct);
            var run = await store.Runs.Where(x => x.Status == "Queued").OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (run is null) return;
            run.Status = "Processing";
            run.StartedAt = now;
            id = run.Id;
        }, ct);
        return id;
    }

    public async Task ProcessAsync(long id, DateTime now, CancellationToken ct)
    {
        var request = await store.Runs.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (sources is not null && !await sources.Channels.AnyAsync(x => x.NormalizedIdentifier == request.Channel && x.Enabled && !x.IsDelete, ct))
            throw new InvalidOperationException("دریافت این منبع در فهرست مشترک متوقف شده است.");
        var result = await collector.CollectChannelAsync(request.Channel, request.MessageLimit, request.Before, ct);
        // Prepare and query external sources before taking the SQL transaction lock.
        await store.ExclusiveAsync(() => EnsureCatalogAsync(ct), ct);
        var catalog = await store.Models.Include(x => x.Trims).AsNoTracking().ToListAsync(ct);
        var messages = result.Messages.Take(request.MessageLimit).ToArray();
        var prepared = new List<(TelegramCollectedMessage Message, int Part, ZeroValidationResult Result)>();
        foreach (var message in messages)
        {
            foreach (var part in ZeroVehicleRules.Split(message.Text))
            {
                ct.ThrowIfCancellationRequested();
                var parsed = ZeroVehicleRules.Validate(message with { Text = part.Text }, request.Channel, catalog, options, now);
                if (parsed.Listing is { } listing)
                {
                    listing.PartIndex = part.Index;
                    var reference = market is null ? new MarketPriceResult("SourceUnavailable", Reason: "CrawlerNotConfigured")
                        : await market.LookupAsync(ReferenceQuery(listing), ct);
                    ApplyReference(listing, reference);
                }
                prepared.Add((message with { Text = part.Text }, part.Index, parsed));
            }
        }
        await store.ExclusiveAsync(async () =>
        {
            var run = await store.Runs.SingleAsync(x => x.Id == id, ct);
            if (run.Status != "Processing") return;
            var externalIds = messages.Select(x => x.ExternalMessageId).ToArray();
            var known = (await store.Listings.Where(x => x.Channel == run.Channel && externalIds.Contains(x.ExternalMessageId))
                .Select(x => new { x.ExternalMessageId, x.PartIndex }).ToListAsync(ct))
                .Select(x => (x.ExternalMessageId, x.PartIndex)).ToHashSet();
            var oldIssues = await store.Issues.Where(x => x.RunId == id).ToListAsync(ct);
            var issueKeys = oldIssues.Select(x => (x.ExternalMessageId, x.PartIndex)).ToHashSet();
            var resolved = new List<(ZeroVehicleIssue Issue, ZeroVehicleListing Listing)>();
            var reasons = new Dictionary<string, int>();
            run.Scanned = messages.Length; run.Extracted = prepared.Count;
            run.Accepted = run.Rejected = run.Duplicates = run.Stored = 0;
            foreach (var item in prepared)
            {
                if (item.Result.Listing is not { } listing)
                {
                    run.Rejected++;
                    var code = item.Result.RejectionCode!;
                    reasons[code] = reasons.GetValueOrDefault(code) + 1;
                    // Used cars are only counted; they are never stored in either vehicle pipeline.
                    if (code is not ("POSITIVE_MILEAGE" or "CONTRADICTORY_CONDITION" or "ZERO_NOT_CONFIRMED")
                        && issueKeys.Add((item.Message.ExternalMessageId, item.Part)))
                        store.Add(new ZeroVehicleIssue { RunId = id, ExternalMessageId = item.Message.ExternalMessageId,
                            PartIndex = item.Part, Code = code, PublishedAt = item.Message.MessageDate, SourceUrl = item.Message.SourceUrl, RawText = item.Message.Text[..Math.Min(8000, item.Message.Text.Length)] });
                    continue;
                }
                run.Accepted++;
                if (!known.Add((listing.ExternalMessageId, listing.PartIndex))) { run.Duplicates++; continue; }
                store.Add(listing); run.Stored++;
                var previousIssue = oldIssues.FirstOrDefault(x => x.ExternalMessageId == listing.ExternalMessageId && x.PartIndex == listing.PartIndex);
                if (previousIssue is not null && previousIssue.ResolvedAt is null) resolved.Add((previousIssue, listing));
            }
            await store.SaveAsync(ct);
            foreach (var (issue, listing) in resolved)
            {
                issue.ResolvedAt = now; issue.ResolvedListingId = listing.Id;
                store.Add(new MarketPriceReview { Scope = "zero-issue", TargetId = issue.Id, Action = "RunRecoveryRematch",
                    Actor = "zero-worker", Reason = "تطبیق هنگام ادامه اجرای متوقف‌شده؛ رد قبلی و متن خام نگهداری شدند.",
                    BeforeJson = JsonSerializer.Serialize(new { issue.Code }), AfterJson = JsonSerializer.Serialize(new { listing.Id }) });
            }
            run.PriceGroups = await RecalculateCoreAsync(now, ct);
            run.NextBefore = result.NextBefore; run.PagesFetched = result.PagesFetched;
            run.StopReason = result.StopReason[..Math.Min(128, result.StopReason.Length)];
            run.RejectionCounts = JsonSerializer.Serialize(reasons);
            run.Status = result.Success ? "Completed" : messages.Length > 0 ? "Partial" : "Failed";
            run.Error = result.Error is { Length: > 1024 } ? result.Error[..1024] : result.Error;
            run.CompletedAt = DateTime.UtcNow;
            var channel = await store.Channels.SingleOrDefaultAsync(x => x.Identifier == run.Channel, ct);
            if (channel is not null)
            {
                channel.LastError = run.Error; channel.LastImportedAt = run.CompletedAt;
                if (result.Success && messages.Length > 0)
                {
                    var ids = messages.Select(x => long.TryParse(x.ExternalMessageId, out var value) ? value : 0).Where(x => x > 0).ToArray();
                    if (ids.Length > 0)
                    {
                        var latest = Math.Max(channel.PendingLatestMessageId ?? 0, ids.Max());
                        if (channel.LatestMessageId.HasValue && ids.Min() > channel.LatestMessageId && messages.Length == request.MessageLimit && result.NextBefore.HasValue)
                        { channel.ScanBefore = result.NextBefore; channel.PendingLatestMessageId = latest; }
                        else
                        { channel.LatestMessageId = Math.Max(channel.LatestMessageId ?? 0, latest); channel.ScanBefore = null; channel.PendingLatestMessageId = null; }
                    }
                }
            }
        }, ct);
    }
    private static string ReferenceQuery(ZeroVehicleListing listing) => listing.OriginalName
        + (listing.Year.HasValue && !Regex.IsMatch(ZeroVehicleRules.Normalize(listing.OriginalName), $@"(?<![0-9]){listing.Year.Value}(?![0-9])") ? $" {listing.Year.Value}" : "");

    private static void ApplyReference(ZeroVehicleListing listing, MarketPriceResult reference)
    {
        // Exact normalized identity only: a unique broader Car.ir variant is still not proof.
        static string Identity(string value) => string.Join(" ", Regex.Replace(ZeroVehicleRules.Normalize(value), @"(?<![\p{L}])(?:مدل|سال)(?![\p{L}])", " ")
            .Replace("اتوماتیک", "اتومات", StringComparison.Ordinal).Replace("دستی", "دنده ای", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        if (reference.Status == "Matched" && (reference.Name is null || Identity(reference.Name) != Identity(ReferenceQuery(listing))))
            reference = reference with { Status = "Ambiguous", Price = null, Reason = "ExactYearAndTrimReferenceRequired" };
        listing.MarketStatus = reference.Status; listing.MarketPrice = reference.Price;
        listing.MarketName = reference.Name; listing.MarketUrl = reference.Url;
        listing.MarketPriceAt = reference.PriceAt; listing.MarketFetchedAt = reference.FetchedAt;
        listing.RejectionReason = reference.Reason;
    }
    public async Task FailAsync(long id, string error, CancellationToken ct)
    {
        await store.ExclusiveAsync(async () =>
        {
            var run = await store.Runs.SingleAsync(x => x.Id == id, ct);
            if (run.Status != "Processing") return;
            run.Status = "Failed"; run.Error = error.Length > 1024 ? error[..1024] : error; run.CompletedAt = DateTime.UtcNow;
            var channel = await store.Channels.SingleOrDefaultAsync(x => x.Identifier == run.Channel, ct);
            if (channel is not null) { channel.LastError = run.Error; channel.LastImportedAt = run.CompletedAt; }
        }, ct);
    }

    public async Task EnsureCatalogAsync(CancellationToken ct)
    {
        var models = await store.Models.Include(x => x.Trims).ToListAsync(ct);
        foreach (var definition in options.Catalog)
        {
            if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 128) throw new InvalidOperationException("Invalid zero vehicle catalog model name.");
            var model = models.FirstOrDefault(x => x.ConfigurationKey == definition.Name || x.Name == definition.Name);
            if (model is null) { model = new() { Name = definition.Name, ConfigurationKey = definition.Name, Aliases = string.Join('\n', definition.Aliases) }; store.Add(model); models.Add(model); }
            model.ConfigurationKey ??= definition.Name;
            foreach (var trimDefinition in definition.Trims.Append(new ZeroTrimDefinition { Name = "نام‌محور" }))
            {
                if (string.IsNullOrWhiteSpace(trimDefinition.Name) || trimDefinition.Name.Length > 128) throw new InvalidOperationException("Invalid zero vehicle catalog trim name.");
                var trim = model.Trims.FirstOrDefault(x => x.ConfigurationKey == trimDefinition.Name || x.Name == trimDefinition.Name);
                if (trim is null) { trim = new() { Model = model, Name = trimDefinition.Name, ConfigurationKey = trimDefinition.Name, Aliases = string.Join('\n', trimDefinition.Aliases) }; model.Trims.Add(trim); store.Add(trim); }
                trim.ConfigurationKey ??= trimDefinition.Name;
                if (trim.Aliases.Length > 2000) throw new InvalidOperationException("Too many zero vehicle trim aliases.");
            }
            if (model.Aliases.Length > 2000) throw new InvalidOperationException("Too many zero vehicle model aliases.");
        }
        await store.SaveAsync(ct);
    }
    public bool WorkerEnabled => configuration.GetValue("ZeroVehicles:WorkerEnabled", true);

    public async Task RequestRecalculationAsync(CancellationToken ct)
    {
        await store.ExclusiveAsync(async () => {
            var state = await store.Settings.SingleOrDefaultAsync(ct);
            if (state is null) { state = new() { DeviationPercent = options.DeviationPercent }; store.Add(state); }
            state.RecalculationRequestedAt = DateTime.UtcNow;
        }, ct);
    }

    public async Task<bool> RequiresRecalculationAsync(DateTime now, CancellationToken ct)
    {
        var state = await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (state?.LastRecalculatedAt is null || state.RecalculationRequestedAt > state.LastRecalculatedAt) return true;
        if (sources is not null && await sources.Channels.AnyAsync(x => x.UpdatedAt > state.LastRecalculatedAt, ct)) return true;
        return await store.Prices.AnyAsync(x => x.PriceReviewStatus != "Rejected" && x.PricingStatus != "Unavailable"
            && x.ManualOverrideUntil <= now && x.ReferencePrice != x.CalculatedMarketPrice, ct);
    }

    private async Task RepairCatalogIssuesAsync(DateTime now, CancellationToken ct)
    {
        var state = await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (state?.RecalculationRequestedAt is null || state.RecalculationRequestedAt <= state.LastRecalculatedAt) return;
        var catalog = await store.Models.AsNoTracking().Include(x => x.Trims).ToListAsync(ct);
        var approved = sources is null ? null : await sources.Channels.AsNoTracking().Where(x => x.Enabled && !x.IsDelete)
            .Select(x => x.NormalizedIdentifier).ToListAsync(ct);
        long cursor = 0;
        while (true)
        {
            var issues = await (from issue in store.Issues.AsNoTracking()
                join run in store.Runs.AsNoTracking() on issue.RunId equals run.Id
                where issue.Id > cursor && issue.ResolvedAt == null && issue.PublishedAt != null
                    && (issue.Code == "MODEL_UNKNOWN_OR_AMBIGUOUS" || issue.Code == "TRIM_UNKNOWN_OR_AMBIGUOUS")
                orderby issue.Id select new { Issue = issue, run.Channel }).Take(500).ToListAsync(ct);
            if (issues.Count == 0) break;
            var repairs = new List<(long IssueId, ZeroVehicleListing Listing)>();
            foreach (var item in issues)
            {
                if (approved is not null && !approved.Contains(item.Channel)) continue;
                var issue = item.Issue;
                var parsed = ZeroVehicleRules.Validate(new(issue.ExternalMessageId, issue.RawText, issue.PublishedAt!.Value, issue.SourceUrl, null),
                    item.Channel, catalog, options, now);
                if (parsed.Listing is not { } listing) continue;
                listing.PartIndex = issue.PartIndex;
                ApplyReference(listing, market is null ? new("SourceUnavailable", Reason: "CrawlerNotConfigured")
                    : await market.LookupAsync(ReferenceQuery(listing), ct));
                repairs.Add((issue.Id, listing));
            }
            if (repairs.Count > 0) await store.ExclusiveAsync(async () => {
                if ((await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.RecalculationRequestedAt != state.RecalculationRequestedAt)
                    throw new TelegramProcessingBusyException("CatalogChangedDuringRepair");
                var linked = new List<(ZeroVehicleIssue Issue, ZeroVehicleListing Listing)>();
                foreach (var repair in repairs)
                {
                    var issue = await store.Issues.SingleAsync(x => x.Id == repair.IssueId, ct);
                    if (issue.ResolvedAt is not null) continue;
                    var row = repair.Listing;
                    var existing = await store.Listings.SingleOrDefaultAsync(x => x.Channel == row.Channel
                        && x.ExternalMessageId == row.ExternalMessageId && x.PartIndex == row.PartIndex, ct);
                    if (existing is null) store.Add(row); else row = existing;
                    linked.Add((issue, row));
                }
                await store.SaveAsync(ct);
                foreach (var (issue, listing) in linked)
                {
                    issue.ResolvedAt = now; issue.ResolvedListingId = listing.Id;
                    store.Add(new MarketPriceReview { Scope = "zero-issue", TargetId = issue.Id, Action = "CatalogRematch",
                        Actor = "catalog-worker", Reason = "تطبیق مجدد پس از اصلاح کاتالوگ؛ متن و تاریخ اصلی حفظ شد.",
                        BeforeJson = JsonSerializer.Serialize(new { issue.Code }), AfterJson = JsonSerializer.Serialize(new { listing.Id, listing.ModelId, listing.TrimId }) });
                }
            }, ct);
            cursor = issues[^1].Issue.Id;
        }
    }

    private async Task RefreshLegacyIdentitiesAsync(DateTime now, CancellationToken ct)
    {
        var catalog = await store.Models.AsNoTracking().Include(x => x.Trims).ToListAsync(ct);
        var catalogStamp = (await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.RecalculationRequestedAt;
        long cursor = 0;
        while (true)
        {
            var rows = await store.Listings.AsNoTracking().Where(x => x.Id > cursor
                && x.ExtractionVersion < ZeroVehicleRules.ExtractionVersion).OrderBy(x => x.Id).Take(500).ToListAsync(ct);
            if (rows.Count == 0) break;
            var repaired = rows.ToDictionary(x => x.Id, x => x.ManuallyMatched ? null : ZeroVehicleRules.Validate(
                new(x.ExternalMessageId, x.RawText, x.PublishedAt, x.SourceUrl, null), x.Channel, catalog, options, now));
            await store.ExclusiveAsync(async () => {
                if ((await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.RecalculationRequestedAt != catalogStamp)
                    throw new TelegramProcessingBusyException("CatalogChangedDuringRepair");
                var ids = rows.Select(x => x.Id).ToArray();
                foreach (var row in await store.Listings.Where(x => ids.Contains(x.Id)).ToListAsync(ct))
                {
                    if (row.ExtractionVersion >= ZeroVehicleRules.ExtractionVersion) continue;
                    row.ExtractionVersion = ZeroVehicleRules.ExtractionVersion;
                    if (row.ManuallyMatched) continue;
                    var parsed = repaired[row.Id];
                    var before = new { row.ModelId, row.TrimId, row.Year, row.YearCalendar, row.PriceDay, row.OriginalName };
                    if (parsed?.Listing is { } updated)
                    {
                        row.ModelId = updated.ModelId; row.TrimId = updated.TrimId; row.Year = updated.Year;
                        row.YearCalendar = updated.YearCalendar; row.PriceDay = updated.PriceDay;
                        row.OriginalName = updated.OriginalName; row.RawCarName = updated.RawCarName;
                        row.PricingStatus = "Pending"; row.MarketStatus = "NotFound"; row.FinalPrice = null;
                    }
                    else { row.PricingStatus = "IdentityNeedsReview"; row.MarketStatus = "NotFound"; row.FinalPrice = null; row.RejectionReason = parsed?.RejectionCode; }
                    store.Add(new MarketPriceReview { Scope = "zero-listing", TargetId = row.Id, Action = "ExtractionRepair",
                        Actor = "zero-extractor-v2", Reason = "تفکیک سال و تیپ با اتکا به متن اصلی؛ داده خام و گروه‌های تاریخی حفظ شدند.",
                        BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(new { row.ModelId, row.TrimId, row.Year, row.YearCalendar, row.PriceDay, row.PricingStatus }) });
                }
            }, ct);
            cursor = rows[^1].Id;
        }
    }

    public async Task<int> RecalculateAsync(DateTime now, CancellationToken ct)
    {
        await RefreshLegacyIdentitiesAsync(now, ct);
        await RepairCatalogIssuesAsync(now, ct);
        var count = 0;
        var names = await store.Listings.AsNoTracking().Where(x => x.PublishedAt >= now.AddDays(-options.MaxPriceAgeDays))
            .Select(x => new { x.OriginalName, x.Year }).Distinct().ToListAsync(ct);
        var references = new Dictionary<(string, int?), MarketPriceResult>();
        foreach (var name in names)
            references[(name.OriginalName, name.Year)] = market is null ? new("SourceUnavailable", Reason: "CrawlerNotConfigured")
                : await market.LookupAsync(ReferenceQuery(new ZeroVehicleListing { OriginalName = name.OriginalName, Year = name.Year }), ct);
        await store.ExclusiveAsync(async () => {
            foreach (var name in names)
            {
                long cursor = 0;
                while (true)
                {
                    var batch = await store.Listings.Where(x => x.Id > cursor && x.OriginalName == name.OriginalName && x.Year == name.Year && x.PublishedAt >= now.AddDays(-options.MaxPriceAgeDays))
                        .OrderBy(x => x.Id).Take(500).ToListAsync(ct);
                    if (batch.Count == 0) break;
                    foreach (var listing in batch.Where(x => x.PricingStatus != "IdentityNeedsReview")) ApplyReference(listing, references[(name.OriginalName, name.Year)]);
                    cursor = batch[^1].Id; await store.SaveAsync(ct);
                    foreach (var listing in batch) store.Detach(listing);
                }
            }
            count = await RecalculateCoreAsync(now, ct);
        }, ct);
        return count;
    }
    private async Task<int> RecalculateCoreAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-options.MaxPriceAgeDays);
        // Mark stale rows in bounded batches; only fresh rows participate in pricing.
        long lastId = 0;
        while (true)
        {
            var stale = await store.Listings.Where(x => x.Id > lastId && (x.PublishedAt < cutoff || x.PublishedAt > now.AddMinutes(5)) && x.PricingStatus != "Stale")
                .OrderBy(x => x.Id).Take(500).ToListAsync(ct);
            if (stale.Count == 0) break;
            foreach (var listing in stale) { listing.PricingStatus = "Stale"; listing.FinalPrice = null; }
            lastId = stale[^1].Id;
            await store.SaveAsync(ct);
            foreach (var listing in stale) store.Detach(listing);
        }
        var deviation = (await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.DeviationPercent ?? options.DeviationPercent;
        var eligibleListings = store.Listings;
        if (sources is not null)
        {
            var approved = await sources.Channels.AsNoTracking().Where(x => x.Enabled && !x.IsDelete)
                .Select(x => x.NormalizedIdentifier).ToListAsync(ct);
            eligibleListings = eligibleListings.Where(x => approved.Contains(x.Channel));
        }
        var identities = await eligibleListings.Where(x => x.PublishedAt >= cutoff && x.PublishedAt <= now.AddMinutes(5))
            .Select(x => new { x.ModelId, x.TrimId, x.Year, x.YearCalendar, x.PriceDay }).Distinct().ToListAsync(ct);
        var existing = await store.Prices.ToListAsync(ct);
        var active = new HashSet<long>();
        foreach (var identity in identities)
        {
            var rows = await eligibleListings.Where(x => x.ModelId == identity.ModelId && x.TrimId == identity.TrimId && x.Year == identity.Year && x.YearCalendar == identity.YearCalendar
                && x.PriceDay == identity.PriceDay && x.PublishedAt >= cutoff && x.PublishedAt <= now.AddMinutes(5))
                .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
            // All sources must resolve to the same reference variant. An unavailable lookup is never included.
            var references = rows.Where(x => x.MarketStatus == "Matched" && x.MarketPrice > 0 && x.MarketPriceAt >= cutoff)
                .OrderByDescending(x => x.MarketFetchedAt).ToArray();
            var validReference = references.Length > 0 && references.Select(x => (x.MarketName, x.MarketUrl)).Distinct().Count() == 1;
            var reference = validReference ? references[0].MarketPrice!.Value : 0;
            var samples = new List<decimal>(); var channels = new HashSet<string>(); var fingerprints = new HashSet<string>();
            foreach (var row in rows)
            {
                row.FinalPrice = null;
                if (row.PricingStatus == "IdentityNeedsReview") continue;
                if (row.AdminDecision == "Rejected") { row.PricingStatus = "NeedsReview"; row.RejectionReason = "AdminRejected"; continue; }
                if (!validReference || row.MarketStatus != "Matched" || (row.MarketPriceAt is null || row.MarketPriceAt < cutoff))
                { row.PricingStatus = "Unavailable"; row.RejectionReason = validReference ? row.MarketStatus : "ReferenceUnavailableOrAmbiguous"; continue; }
                decimal[] candidates;
                try { candidates = JsonSerializer.Deserialize<decimal[]>(row.PriceCandidates) ?? []; }
                catch (JsonException) { candidates = []; }
                if (candidates.Length == 0) candidates = [row.PriceToman]; // Existing rows predate candidate provenance.
                var accepted = candidates.Where(x => x > 0 && Math.Abs(x - reference) / reference * 100 <= deviation).ToArray();
                if (accepted.Length != 1)
                {
                    row.PricingStatus = accepted.Length > 1 ? "AmbiguousPrice" : "Outlier";
                    row.RejectionReason = accepted.Length > 1 ? "MultipleUnitsFitReference" : $"OutsideMarketTolerance:{deviation}%";
                    continue;
                }
                row.PriceToman = accepted[0];
                if (!fingerprints.Add(row.Fingerprint)) { row.PricingStatus = "DuplicateContent"; row.RejectionReason = "RepostedContent"; continue; }
                // One latest valid quote per channel prevents one dealer dominating the market average.
                if (!channels.Add(row.Channel)) { row.PricingStatus = "Superseded"; row.RejectionReason = "NewerValidChannelQuote"; continue; }
                row.PricingStatus = "Included"; row.RejectionReason = null; samples.Add(row.PriceToman);
            }
            if (!validReference) { await store.SaveAsync(ct); continue; }
            var final = decimal.Round((reference + samples.Sum()) / (samples.Count + 1), 2);
            foreach (var row in rows.Where(x => x.PricingStatus == "Included")) row.FinalPrice = final;
            var group = existing.SingleOrDefault(x => x.ModelId == identity.ModelId && x.TrimId == identity.TrimId && x.Year == identity.Year
                && x.YearCalendar == identity.YearCalendar && x.PriceDay == identity.PriceDay);
            if (group is null) { group = new() { ModelId = identity.ModelId, TrimId = identity.TrimId, Year = identity.Year,
                YearCalendar = identity.YearCalendar, PriceDay = identity.PriceDay }; store.Add(group); existing.Add(group); }
            group.CalculatedMarketPrice = final;
            group.ReferencePrice = MarketPricePolicy.Effective(final, group.ManualOverridePrice, group.ManualOverrideUntil, group.PriceReviewStatus, now);
            group.MarketPrice = reference;
            group.PricingStatus = samples.Count == 0 ? "ReferenceOnly" : "Priced";
            group.MinPrice = samples.Append(reference).Min(); group.MaxPrice = samples.Append(reference).Max();
            group.SampleCount = samples.Count; group.SourceCount = channels.Count;
            group.Confidence = channels.Count < 2 ? "Low" : channels.Count < 5 ? "Medium" : "High"; group.CalculatedAt = now;
            await store.SaveAsync(ct);
            active.Add(group.Id);
        }
        foreach (var group in existing.Where(x => !active.Contains(x.Id))) group.PricingStatus = "Unavailable";
        var calculationState = await store.Settings.SingleOrDefaultAsync(ct);
        if (calculationState is null) { calculationState = new() { DeviationPercent = options.DeviationPercent }; store.Add(calculationState); }
        calculationState.LastRecalculatedAt = now;
        return active.Count;
    }
    public async Task<ZeroPage<ZeroListingDto>> ListingsAsync(string? search, long? modelId, long? trimId, string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination(page, pageSize);
        var query = store.Listings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Model.Name.Contains(term) || x.Trim.Name.Contains(term) || x.Channel.Contains(term)); }
        if (modelId.HasValue) query = query.Where(x => x.ModelId == modelId);
        if (trimId.HasValue) query = query.Where(x => x.TrimId == trimId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.PricingStatus == status);
        return new(await ListingProjection(query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Skip(page * pageSize).Take(pageSize)).ToListAsync(ct), await query.CountAsync(ct), page, pageSize);
    }
    public async Task<ZeroListingDetail?> DetailAsync(long id, CancellationToken ct)
    {
        var listing = await ListingProjection(store.Listings.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct);
        return listing is null ? null : new(listing, await store.Listings.Where(x => x.Id == id).Select(x => x.RawText).SingleAsync(ct));
    }
    public async Task<ZeroPage<ZeroPriceDto>> PricesAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination(page, pageSize);
        var query = store.Prices.AsNoTracking().Where(x => x.PriceReviewStatus != "Rejected" && (x.PricingStatus == "Priced" || x.PricingStatus == "ReferenceOnly"));
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Model.Name.Contains(term) || x.Trim.Name.Contains(term)); }
        return new(await query.OrderByDescending(x => x.CalculatedAt).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize)
            .Select(x => new ZeroPriceDto(x.Id, x.ModelId, x.Model.Name, x.TrimId, x.Trim.Name, x.ReferencePrice, x.MinPrice, x.MaxPrice, x.SampleCount, x.SourceCount, x.Confidence, x.CalculatedAt, x.MarketPrice, x.PricingStatus, x.Year, x.YearCalendar, x.PriceDay)).ToListAsync(ct), await query.CountAsync(ct), page, pageSize);
    }
    public async Task<ZeroPage<ZeroListingDto>?> MembersAsync(long id, int page, int pageSize, CancellationToken ct)
    {
        var group = await store.Prices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (group is null) return null;
        (page, pageSize) = Pagination(page, pageSize);
        var query = store.Listings.AsNoTracking().Where(x => x.ModelId == group.ModelId && x.TrimId == group.TrimId
            && x.Year == group.Year && x.YearCalendar == group.YearCalendar && x.PriceDay == group.PriceDay && x.PricingStatus == "Included");
        return new(await ListingProjection(query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Skip(page * pageSize).Take(pageSize)).ToListAsync(ct), await query.CountAsync(ct), page, pageSize);
    }
    public async Task<IReadOnlyList<ZeroCatalogDto>> CatalogAsync(CancellationToken ct)
    {
        var models = await store.Models.AsNoTracking().Include(x => x.Trims).OrderBy(x => x.Name).ToListAsync(ct);
        return models.Select(x => new ZeroCatalogDto(x.Id, x.Name, x.Trims.Select(t => new ZeroTrimDto(t.Id, t.Name)).ToArray())).ToArray();
    }
    public async Task<IReadOnlyList<ZeroVehicleChannel>> ChannelsAsync(CancellationToken ct)
    {
        var checkpoints = await store.Channels.AsNoTracking().ToListAsync(ct);
        if (sources is null) return checkpoints.OrderBy(x => x.Identifier).ToArray();
        var shared = await sources.Channels.AsNoTracking().Where(x => !x.IsDelete).OrderBy(x => x.NormalizedIdentifier).ToListAsync(ct);
        return shared.Select(x => new ZeroVehicleChannel {
            Id = x.Id, Identifier = x.NormalizedIdentifier, Enabled = x.Enabled,
            LastImportedAt = checkpoints.FirstOrDefault(c => c.Identifier == x.NormalizedIdentifier)?.LastImportedAt,
            LastError = checkpoints.FirstOrDefault(c => c.Identifier == x.NormalizedIdentifier)?.LastError ?? x.LastError,
            LatestMessageId = checkpoints.FirstOrDefault(c => c.Identifier == x.NormalizedIdentifier)?.LatestMessageId,
            ScanBefore = checkpoints.FirstOrDefault(c => c.Identifier == x.NormalizedIdentifier)?.ScanBefore
        }).ToArray();
    }

    public async Task ScheduleCollectionAsync(DateTime now, CancellationToken ct)
    {
        if (sources is null) return;
        var due = now.AddMinutes(-Math.Max(1, configuration.GetValue("Telegram:CollectionIntervalMinutes", 300)));
        var enabled = await sources.Channels.AsNoTracking().Where(x => x.Enabled && !x.IsDelete
            && x.Username != "").Select(x => x.NormalizedIdentifier).ToListAsync(ct);
        foreach (var identifier in enabled)
        {
            if (await store.Channels.AnyAsync(x => x.Identifier == identifier && x.LastImportedAt >= due, ct)) continue;
            if (await store.Runs.AnyAsync(x => x.Channel == identifier && (x.Status == "Queued" || x.Status == "Processing"), ct)) continue;
            try { await EnqueueAsync(new(identifier, 500), ct); }
            catch (InvalidOperationException) { /* Another instance scheduled it under the SQL lock. */ }
            catch (ArgumentException) { /* Source was disabled concurrently. */ }
        }
    }
    public async Task<IReadOnlyList<ZeroChannelResult>> AddChannelsAsync(string identifiers, CancellationToken ct)
    {
        if (sourceManagement is not null)
        {
            var imported = await sourceManagement.ImportAsync(new BulkTelegramChannelImportRequest(identifiers), ct);
            return Regex.Split(identifiers, @"[\s,،;]+").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => new ZeroChannelResult(x, null, TelegramChannelService.Normalize(x) is null ? "شناسه نامعتبر است." : null)).ToArray();
        }
        if (identifiers.Length > 30000) throw new ArgumentException("حداکثر ۲۰۰ کانال وارد کنید.");
        var results = new List<ZeroChannelResult>();
        foreach (var token in Regex.Split(identifiers, @"[\s,،;]+").Where(x => x.Length > 0).Take(201))
        {
            if (results.Count == 200) throw new ArgumentException("حداکثر ۲۰۰ کانال وارد کنید.");
            string identifier;
            try { identifier = NormalizeChannel(token); }
            catch (ArgumentException ex) { results.Add(new(token, null, ex.Message)); continue; }
            await store.ExclusiveAsync(async () => {
                if (!await store.Channels.AnyAsync(x => x.Identifier == identifier, ct)) store.Add(new ZeroVehicleChannel { Identifier = identifier });
            }, ct);
            results.Add(new(identifier, null, null));
        }
        return results;
    }
    public async Task<bool> ChangeChannelAsync(long id, bool? enabled, CancellationToken ct)
    {
        if (sourceManagement is not null)
            return enabled.HasValue ? await sourceManagement.SetEnabledAsync(id, enabled.Value, ct)
                : await sourceManagement.DeleteAsync(id, ct);
        var found = false;
        await store.ExclusiveAsync(async () => {
            var channel = await store.Channels.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (channel is null) return;
            found = true;
            if (enabled.HasValue) channel.Enabled = enabled.Value; else store.Remove(channel);
        }, ct);
        return found;
    }
    public async Task<IReadOnlyList<ZeroChannelResult>> ImportAllAsync(int messageLimit, CancellationToken ct)
    {
        var channels = (await ChannelsAsync(ct)).Where(x => x.Enabled && !x.Identifier.StartsWith("+", StringComparison.Ordinal)).Select(x => x.Identifier).ToList();
        var result = new List<ZeroChannelResult>();
        foreach (var channel in channels)
        {
            try { result.Add(new(channel, await EnqueueAsync(new(channel, messageLimit), ct), null)); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TelegramProcessingBusyException)
            { result.Add(new(channel, null, ex.Message)); }
        }
        return result;
    }
    public async Task<ZeroPage<ZeroVehicleIssue>> IssuesAsync(long runId, int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 0, 100000);
        var query = store.Issues.AsNoTracking().Where(x => x.RunId == runId);
        return new(await query.OrderBy(x => x.Id).Skip(page * 20).Take(20).ToListAsync(ct), await query.CountAsync(ct), page, 20);
    }
    public async Task<decimal> SettingsAsync(decimal? deviation, CancellationToken ct)
    {
        if (deviation is < 1 or > 50) throw new ArgumentException("درصد اختلاف باید بین ۱ و ۵۰ باشد.");
        if (deviation is not null) await store.ExclusiveAsync(async () => {
            var setting = await store.Settings.SingleOrDefaultAsync(ct);
            if (setting is null) { setting = new(); store.Add(setting); }
            setting.DeviationPercent = deviation.Value;
            setting.RecalculationRequestedAt = DateTime.UtcNow;
        }, ct);
        return (await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.DeviationPercent ?? options.DeviationPercent;
    }
    private static (int Page, int Size) Pagination(int page, int pageSize) => (Math.Clamp(page, 0, 100000), Math.Clamp(pageSize, 1, 100));
}
