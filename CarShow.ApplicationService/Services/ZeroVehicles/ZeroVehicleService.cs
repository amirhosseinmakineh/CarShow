using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace CarShow.ApplicationService.Services.ZeroVehicles;

public sealed class ZeroVehicleService(IZeroVehicleStore store, ITelegramCollector collector, IConfiguration configuration)
{
    private readonly ZeroVehicleOptions options = ZeroVehicleOptions.Read(configuration);
    private static readonly Expression<Func<ZeroVehicleImportRun, ZeroImportDto>> RunProjection = run => new(
        run.Id, run.Channel, run.Status, run.MessageLimit, run.Before, run.NextBefore, run.PagesFetched,
        run.StopReason, run.CreatedAt, run.CompletedAt, run.Scanned, run.Accepted, run.Rejected,
        run.Duplicates, run.Stored, run.PriceGroups, run.RejectionCounts, run.Error);
    private IQueryable<ZeroListingDto> ListingProjection(IQueryable<ZeroVehicleListing> query) => query.Select(x => new ZeroListingDto(
        x.Id, x.ModelId, x.Model.Name, x.TrimId, x.Trim.Name, x.OriginalName, x.PriceToman, x.Year,
        x.PublishedAt, x.ReceivedAt, x.Channel, x.ExternalMessageId, x.SourceUrl, x.PricingStatus,
        store.Prices.Where(p => p.ModelId == x.ModelId && p.TrimId == x.TrimId).Select(p => (decimal?)p.ReferencePrice).FirstOrDefault()));

    public async Task<ZeroImportDto> EnqueueAsync(ZeroImportRequest request, CancellationToken ct)
    {
        var channel = NormalizeChannel(request.Channel);
        if (request.MessageLimit is < 1 or > 500 || request.Before is <= 0) throw new ArgumentException("تعداد پیام باید بین ۱ و ۵۰۰ و نشانگر ادامه مثبت باشد.");
        ZeroVehicleImportRun? run = null;
        await store.ExclusiveAsync(async () =>
        {
            run = await store.Runs.FirstOrDefaultAsync(x => x.Channel == channel && (x.Status == "Queued" || x.Status == "Processing"), ct);
            if (run is not null) throw new InvalidOperationException("دریافت دیگری برای این کانال در حال اجراست.");
            run = new() { Channel = channel, MessageLimit = request.MessageLimit, Before = request.Before, CreatedAt = DateTime.UtcNow };
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
        var result = await collector.CollectChannelAsync(request.Channel, request.MessageLimit, request.Before, ct);
        var receivedAt = DateTime.UtcNow;
        await store.ExclusiveAsync(async () =>
        {
            var run = await store.Runs.SingleAsync(x => x.Id == id, ct);
            if (run.Status != "Processing") return;
            await EnsureCatalogAsync(ct);
            var catalog = await store.Models.Include(x => x.Trims).ToListAsync(ct);
            // The collector and API both cap a run at 500 messages. Invalid bodies never reach Add().
            var messages = result.Messages.Take(run.MessageLimit).ToArray();
            var externalIds = messages.Select(x => x.ExternalMessageId).ToArray();
            var known = (await store.Listings.Where(x => x.Channel == run.Channel && externalIds.Contains(x.ExternalMessageId))
                .Select(x => x.ExternalMessageId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var reasons = new Dictionary<string, int>();
            var touched = new HashSet<(long, long)>();
            run.Scanned = run.Accepted = run.Rejected = run.Duplicates = run.Stored = 0;
            foreach (var message in messages)
            {
                ct.ThrowIfCancellationRequested();
                run.Scanned++;
                var parsed = ZeroVehicleRules.Validate(message, run.Channel, catalog, options, now);
                if (parsed.Listing is null)
                {
                    run.Rejected++;
                    var code = parsed.RejectionCode!;
                    reasons[code] = reasons.GetValueOrDefault(code) + 1;
                    continue;
                }
                run.Accepted++;
                touched.Add((parsed.Listing.ModelId, parsed.Listing.TrimId));
                if (!known.Add(message.ExternalMessageId)) { run.Duplicates++; continue; }
                parsed.Listing.ReceivedAt = receivedAt;
                store.Add(parsed.Listing);
                run.Stored++;
            }
            await store.SaveAsync(ct);
            await RecalculateCoreAsync(now, ct);
            run.PriceGroups = (await store.Prices.Select(x => new { x.ModelId, x.TrimId }).ToListAsync(ct)).Count(x => touched.Contains((x.ModelId, x.TrimId)));
            run.NextBefore = result.NextBefore;
            run.PagesFetched = result.PagesFetched;
            run.StopReason = result.StopReason.Length > 128 ? result.StopReason[..128] : result.StopReason;
            run.RejectionCounts = JsonSerializer.Serialize(reasons);
            run.Status = result.Success ? "Completed" : messages.Length > 0 ? "Partial" : "Failed";
            run.Error = result.Error is { Length: > 1024 } ? result.Error[..1024] : result.Error;
            run.CompletedAt = DateTime.UtcNow;
        }, ct);
    }
    public async Task FailAsync(long id, string error, CancellationToken ct)
    {
        await store.ExclusiveAsync(async () =>
        {
            var run = await store.Runs.SingleAsync(x => x.Id == id, ct);
            if (run.Status != "Processing") return;
            run.Status = "Failed"; run.Error = error.Length > 1024 ? error[..1024] : error; run.CompletedAt = DateTime.UtcNow;
        }, ct);
    }

    public async Task EnsureCatalogAsync(CancellationToken ct)
    {
        var models = await store.Models.Include(x => x.Trims).ToListAsync(ct);
        foreach (var definition in options.Catalog)
        {
            if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 128) throw new InvalidOperationException("Invalid zero vehicle catalog model name.");
            var model = models.FirstOrDefault(x => x.Name == definition.Name);
            if (model is null) { model = new() { Name = definition.Name }; store.Add(model); models.Add(model); }
            model.Aliases = string.Join('\n', definition.Aliases);
            foreach (var trimDefinition in definition.Trims)
            {
                if (string.IsNullOrWhiteSpace(trimDefinition.Name) || trimDefinition.Name.Length > 128) throw new InvalidOperationException("Invalid zero vehicle catalog trim name.");
                var trim = model.Trims.FirstOrDefault(x => x.Name == trimDefinition.Name);
                if (trim is null) { trim = new() { Model = model, Name = trimDefinition.Name }; model.Trims.Add(trim); store.Add(trim); }
                trim.Aliases = string.Join('\n', trimDefinition.Aliases);
                if (trim.Aliases.Length > 2000) throw new InvalidOperationException("Too many zero vehicle trim aliases.");
            }
            if (model.Aliases.Length > 2000) throw new InvalidOperationException("Too many zero vehicle model aliases.");
        }
        await store.SaveAsync(ct);
    }
    public async Task<int> RecalculateAsync(DateTime now, CancellationToken ct)
    {
        var count = 0;
        await store.ExclusiveAsync(async () => count = await RecalculateCoreAsync(now, ct), ct);
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
            foreach (var listing in stale) listing.PricingStatus = "Stale";
            lastId = stale[^1].Id;
            await store.SaveAsync(ct);
            foreach (var listing in stale) store.Detach(listing);
        }
        var identities = await store.Listings.Where(x => x.PublishedAt >= cutoff && x.PublishedAt <= now.AddMinutes(5))
            .Select(x => new { x.ModelId, x.TrimId }).Distinct().ToListAsync(ct);
        var existing = await store.Prices.ToListAsync(ct);
        var active = new HashSet<(long, long)>();
        foreach (var identity in identities)
        {
            // Keep only compact pricing fields in memory; never materialize all raw bodies.
            var rows = store.Listings.Where(x => x.ModelId == identity.ModelId && x.TrimId == identity.TrimId && x.PublishedAt >= cutoff && x.PublishedAt <= now.AddMinutes(5));
            var unique = new Dictionary<string, (long Id, decimal Price, string Channel)>(StringComparer.Ordinal);
            long cursor = 0;
            while (true)
            {
                var batch = await rows.AsNoTracking().Where(x => x.Id > cursor).OrderBy(x => x.Id).Take(500)
                    .Select(x => new { x.Id, x.Fingerprint, x.PriceToman, x.Channel }).ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var item in batch)
                    if (item.PriceToman >= options.MinimumPriceToman && item.PriceToman <= options.MaximumPriceToman)
                        unique.TryAdd(item.Fingerprint, (item.Id, item.PriceToman, item.Channel));
                cursor = batch[^1].Id;
            }
            var candidates = unique.Values.ToArray();
            var center = candidates.Length == 0 ? 0 : ZeroVehicleRules.Median(candidates.Select(x => x.Price));
            var samples = candidates.Where(x => candidates.Length < 3 || (x.Price >= center / 2m && x.Price <= center * 2m)).ToArray();
            var originalIds = candidates.Select(x => x.Id).ToHashSet();
            var sampleIds = samples.Select(x => x.Id).ToHashSet();
            cursor = 0;
            while (true)
            {
                var batch = await rows.Where(x => x.Id > cursor).OrderBy(x => x.Id).Take(500).ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var listing in batch)
                    listing.PricingStatus = listing.PriceToman < options.MinimumPriceToman || listing.PriceToman > options.MaximumPriceToman
                        ? "InvalidPrice" : !originalIds.Contains(listing.Id) ? "DuplicateContent" : sampleIds.Contains(listing.Id) ? "Included" : "Outlier";
                cursor = batch[^1].Id;
                await store.SaveAsync(ct);
                foreach (var listing in batch) store.Detach(listing);
            }
            if (samples.Length == 0) continue;
            active.Add((identity.ModelId, identity.TrimId));
            var group = existing.SingleOrDefault(x => x.ModelId == identity.ModelId && x.TrimId == identity.TrimId);
            if (group is null) { group = new() { ModelId = identity.ModelId, TrimId = identity.TrimId }; store.Add(group); }
            group.ReferencePrice = ZeroVehicleRules.Median(samples.Select(x => x.Price));
            group.MinPrice = samples.Min(x => x.Price); group.MaxPrice = samples.Max(x => x.Price);
            group.SampleCount = samples.Length; group.SourceCount = samples.Select(x => x.Channel).Distinct().Count();
            group.Confidence = group.SourceCount == 1 ? "Low" : group.SourceCount < 5 ? "Medium" : "High";
            group.CalculatedAt = now;
            await store.SaveAsync(ct);
        }
        foreach (var group in existing.Where(x => !active.Contains((x.ModelId, x.TrimId)))) store.Remove(group);
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
        var query = store.Prices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Model.Name.Contains(term) || x.Trim.Name.Contains(term)); }
        return new(await query.OrderByDescending(x => x.CalculatedAt).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize)
            .Select(x => new ZeroPriceDto(x.Id, x.ModelId, x.Model.Name, x.TrimId, x.Trim.Name, x.ReferencePrice, x.MinPrice, x.MaxPrice, x.SampleCount, x.SourceCount, x.Confidence, x.CalculatedAt)).ToListAsync(ct), await query.CountAsync(ct), page, pageSize);
    }
    public async Task<ZeroPage<ZeroListingDto>?> MembersAsync(long id, int page, int pageSize, CancellationToken ct)
    {
        var group = await store.Prices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return group is null ? null : await ListingsAsync(null, group.ModelId, group.TrimId, "Included", page, pageSize, ct);
    }
    public async Task<IReadOnlyList<ZeroCatalogDto>> CatalogAsync(CancellationToken ct)
    {
        var models = await store.Models.AsNoTracking().Include(x => x.Trims).OrderBy(x => x.Name).ToListAsync(ct);
        return models.Select(x => new ZeroCatalogDto(x.Id, x.Name, x.Trims.Select(t => new ZeroTrimDto(t.Id, t.Name)).ToArray())).ToArray();
    }
    private static (int Page, int Size) Pagination(int page, int pageSize) => (Math.Clamp(page, 0, 100000), Math.Clamp(pageSize, 1, 100));
}
