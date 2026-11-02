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

public sealed class ZeroVehicleService(IZeroVehicleStore store, ITelegramCollector collector, IConfiguration configuration, ICarIrMarketPriceCrawler? market = null)
{
    private readonly ZeroVehicleOptions options = ZeroVehicleOptions.Read(configuration);
    private static readonly Expression<Func<ZeroVehicleImportRun, ZeroImportDto>> RunProjection = run => new(
        run.Id, run.Channel, run.Status, run.MessageLimit, run.Before, run.NextBefore, run.PagesFetched,
        run.StopReason, run.CreatedAt, run.CompletedAt, run.Scanned, run.Accepted, run.Rejected,
        run.Duplicates, run.Stored, run.PriceGroups, run.RejectionCounts, run.Error, run.Extracted);
    private IQueryable<ZeroListingDto> ListingProjection(IQueryable<ZeroVehicleListing> query) => query.Select(x => new ZeroListingDto(
        x.Id, x.ModelId, x.Model.Name, x.TrimId, x.Trim.Name, x.OriginalName, x.PriceToman, x.Year,
        x.PublishedAt, x.ReceivedAt, x.Channel, x.ExternalMessageId, x.SourceUrl, x.PricingStatus, x.MarketPrice, x.FinalPrice, x.MarketStatus, x.MarketName, x.MarketUrl,
        x.MarketPriceAt, x.MarketFetchedAt, x.RejectionReason, x.PartIndex, x.RawPrice));

    public async Task<ZeroImportDto> EnqueueAsync(ZeroImportRequest request, CancellationToken ct)
    {
        var channel = NormalizeChannel(request.Channel);
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
                        : await market.LookupAsync(listing.OriginalName, ct);
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
            foreach (var issue in oldIssues) store.Remove(issue);
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
                    if (code is not ("POSITIVE_MILEAGE" or "CONTRADICTORY_CONDITION" or "ZERO_NOT_CONFIRMED"))
                        store.Add(new ZeroVehicleIssue { RunId = id, ExternalMessageId = item.Message.ExternalMessageId,
                            PartIndex = item.Part, Code = code, RawText = item.Message.Text[..Math.Min(8000, item.Message.Text.Length)] });
                    continue;
                }
                run.Accepted++;
                if (!known.Add((listing.ExternalMessageId, listing.PartIndex))) { run.Duplicates++; continue; }
                store.Add(listing); run.Stored++;
            }
            await store.SaveAsync(ct);
            await RecalculateCoreAsync(now, ct);
            run.PriceGroups = await store.Prices.CountAsync(ct);
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
    private static void ApplyReference(ZeroVehicleListing listing, MarketPriceResult reference)
    {
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
            var model = models.FirstOrDefault(x => x.Name == definition.Name);
            if (model is null) { model = new() { Name = definition.Name }; store.Add(model); models.Add(model); }
            model.Aliases = string.Join('\n', definition.Aliases);
            foreach (var trimDefinition in definition.Trims.Append(new ZeroTrimDefinition { Name = "نام‌محور" }))
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
        var names = await store.Listings.AsNoTracking().Where(x => x.PublishedAt >= now.AddDays(-options.MaxPriceAgeDays))
            .Select(x => x.OriginalName).Distinct().ToListAsync(ct);
        var references = new Dictionary<string, MarketPriceResult>();
        foreach (var name in names)
            references[name] = market is null ? new("SourceUnavailable", Reason: "CrawlerNotConfigured") : await market.LookupAsync(name, ct);
        await store.ExclusiveAsync(async () => {
            foreach (var name in names)
            {
                long cursor = 0;
                while (true)
                {
                    var batch = await store.Listings.Where(x => x.Id > cursor && x.OriginalName == name && x.PublishedAt >= now.AddDays(-options.MaxPriceAgeDays))
                        .OrderBy(x => x.Id).Take(500).ToListAsync(ct);
                    if (batch.Count == 0) break;
                    foreach (var listing in batch) ApplyReference(listing, references[name]);
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
        var identities = await store.Listings.Where(x => x.PublishedAt >= cutoff && x.PublishedAt <= now.AddMinutes(5))
            .Select(x => new { x.ModelId, x.TrimId }).Distinct().ToListAsync(ct);
        var existing = await store.Prices.ToListAsync(ct);
        var active = new HashSet<(long, long)>();
        foreach (var identity in identities)
        {
            var rows = await store.Listings.Where(x => x.ModelId == identity.ModelId && x.TrimId == identity.TrimId && x.PublishedAt >= cutoff && x.PublishedAt <= now.AddMinutes(5))
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
            foreach (var row in rows) row.FinalPrice = final;
            active.Add((identity.ModelId, identity.TrimId));
            var group = existing.SingleOrDefault(x => x.ModelId == identity.ModelId && x.TrimId == identity.TrimId);
            if (group is null) { group = new() { ModelId = identity.ModelId, TrimId = identity.TrimId }; store.Add(group); }
            group.ReferencePrice = final; group.MarketPrice = reference;
            group.PricingStatus = samples.Count == 0 ? "ReferenceOnly" : "Priced";
            group.MinPrice = samples.Append(reference).Min(); group.MaxPrice = samples.Append(reference).Max();
            group.SampleCount = samples.Count; group.SourceCount = channels.Count;
            group.Confidence = channels.Count < 2 ? "Low" : channels.Count < 5 ? "Medium" : "High"; group.CalculatedAt = now;
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
        var query = store.Prices.AsNoTracking().Where(x => x.PricingStatus == "Priced" || x.PricingStatus == "ReferenceOnly");
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Model.Name.Contains(term) || x.Trim.Name.Contains(term)); }
        return new(await query.OrderByDescending(x => x.CalculatedAt).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize)
            .Select(x => new ZeroPriceDto(x.Id, x.ModelId, x.Model.Name, x.TrimId, x.Trim.Name, x.ReferencePrice, x.MinPrice, x.MaxPrice, x.SampleCount, x.SourceCount, x.Confidence, x.CalculatedAt, x.MarketPrice, x.PricingStatus)).ToListAsync(ct), await query.CountAsync(ct), page, pageSize);
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
    public async Task<IReadOnlyList<ZeroVehicleChannel>> ChannelsAsync(CancellationToken ct) => await store.Channels.AsNoTracking().OrderBy(x => x.Identifier).ToListAsync(ct);
    public async Task<IReadOnlyList<ZeroChannelResult>> AddChannelsAsync(string identifiers, CancellationToken ct)
    {
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
        var channels = await store.Channels.AsNoTracking().Where(x => x.Enabled).Select(x => x.Identifier).ToListAsync(ct);
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
        }, ct);
        return (await store.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.DeviationPercent ?? options.DeviationPercent;
    }
    private static (int Page, int Size) Pagination(int page, int pageSize) => (Math.Clamp(page, 0, 100000), Math.Clamp(pageSize, 1, 100));
}
