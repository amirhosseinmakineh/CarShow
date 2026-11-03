using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models.ZeroVehicles;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;

namespace CarShow.Infrastracture.ZeroVehicles;

// Singleton: one bounded page cache and one flight shared by all lookups. No price catalogue is persisted.
public sealed class CarIrMarketPriceCrawler(IHttpClientFactory clients, IConfiguration configuration) : ICarIrMarketPriceCrawler
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<Entry>? cached;
    private DateTime expires;
    private DateTime cacheFetchedAt;
    private DateTime nextRequest;
    public sealed record Entry(string Name, decimal? Price, DateTime? PriceAt, string Url);
    public async Task<MarketPriceResult> LookupAsync(string vehicleName, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            if (cached is null || now >= expires)
            {
                if (nextRequest > now) await Task.Delay(nextRequest - now, ct);
                nextRequest = DateTime.UtcNow.AddSeconds(Math.Clamp(configuration.GetValue("ZeroVehicles:CarIr:MinRequestSeconds", 2), 1, 60));
                var client = clients.CreateClient("CarIr");
                var robots = await DownloadAsync(client, "https://car.ir/robots.txt", ct);
                if (!AllowsPrices(robots)) return new("SourceUnavailable", FetchedAt: now, Reason: "RobotsDisallowed");
                var html = await DownloadAsync(client, "https://car.ir/prices", ct);
                cached = Parse(html, DateTime.UtcNow);
                if (cached.Count == 0) { cached = null; return new("SourceUnavailable", FetchedAt: now, Reason: "SourceStructureChanged"); }
                cacheFetchedAt = DateTime.UtcNow;
                expires = DateTime.UtcNow.AddMinutes(Math.Clamp(configuration.GetValue("ZeroVehicles:CarIr:CacheMinutes", 15), 1, 120));
            }
            return Match(vehicleName, cached, DateTime.UtcNow,
                Math.Clamp(configuration.GetValue("ZeroVehicles:CarIr:MaxPriceAgeDays", 7), 1, 30)) with { FetchedAt = cacheFetchedAt };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("SourceUnavailable", FetchedAt: DateTime.UtcNow, Reason: "Timeout"); }
        catch (HttpRequestException ex) { return new("SourceUnavailable", FetchedAt: DateTime.UtcNow, Reason: ex.StatusCode?.ToString() ?? "NetworkError"); }
        finally { gate.Release(); }
    }
    private static async Task<string> DownloadAsync(HttpClient client, string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await client.GetAsync(url, ct);
            // Fail rather than circumvent bot protection or a rate limit.
            if (attempt == 0 && (int)response.StatusCode >= 500)
            { await Task.Delay(1000, ct); continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 4_000_000) throw new HttpRequestException("SourceTooLarge");
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Length > 4_000_000) throw new HttpRequestException("SourceTooLarge");
            return body;
        }
    }
    public static bool AllowsPrices(string robots)
    {
        // Only the wildcard group applies to this named crawler. An unreadable policy fails closed.
        var applies = false; var sawAgent = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            { sawAgent = true; applies = line[11..].Trim() is "*" or "CarShowMarketPrice"; }
            if (applies && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var path = line[9..].Trim();
                if (path.Length > 0 && Regex.IsMatch("/prices", "^" + Regex.Escape(path).Replace("\\*", ".*").Replace("\\$", "$"))) return false;
            }
        }
        return sawAgent;
    }
    public static IReadOnlyList<Entry> Parse(string html, DateTime fetchedAt)
    {
        var document = new HtmlDocument(); document.LoadHtml(html);
        var result = new List<Entry>();
        foreach (var table in document.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
        {
            var headings = table.SelectNodes(".//thead/tr/th")?.Select(x => Normalize(x.InnerText)).ToArray();
            var marketIndex = headings is null ? -1 : Array.FindIndex(headings, x => x == "قیمت بازار");
            if (marketIndex < 0 || headings![0] != "نام خودرو") continue;
            foreach (var row in table.SelectNodes(".//tbody/tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = row.SelectNodes("./td");
                if (cells is null || cells.Count <= marketIndex) continue;
                var link = cells[0].SelectSingleNode(".//a[@href]");
                if (link is null) continue;
                // Include badges outside the link (e.g. single/dual differential), not just its title.
                var name = Clean(cells[0]);
                var priceNode = cells[marketIndex].SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' price-cell__price ')]");
                decimal? price = null;
                if (priceNode is not null)
                {
                    var amount = Regex.Match(Normalize(priceNode.InnerText), @"^([0-9,٬]+)\s*تومان");
                    if (decimal.TryParse(amount.Groups[1].Value.Replace(",", "").Replace("٬", ""), out var parsed) && parsed > 0) price = parsed;
                }
                var age = cells[marketIndex].SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' price-cell__data ')]/span[last()]");
                var date = age is null ? null : ParseAge(Normalize(age.InnerText), fetchedAt);
                var href = link.GetAttributeValue("href", "");
                if (!Uri.TryCreate(new Uri("https://car.ir"), href, out var url) || url.Host != "car.ir" || url.Scheme != "https") continue;
                result.Add(new(name, price, date, url.ToString()));
            }
        }
        return result.Distinct().ToArray();
    }
    private static string Clean(HtmlNode node) => string.Join(" ", node.DescendantsAndSelf()
        .Where(x => x.NodeType == HtmlNodeType.Text).Select(x => HtmlEntity.DeEntitize(x.InnerText).Trim()).Where(x => x.Length > 0));
    public static DateTime? ParseAge(string text, DateTime fetchedAt)
    {
        if (text.Contains("امروز") || text.Contains("لحظ") || text.Contains("اکنون")) return fetchedAt;
        if (text.Contains("دیروز")) return fetchedAt.AddDays(-1);
        var match = Regex.Match(text, @"(?<n>[0-9]+|یک)\s*(?<unit>دقیقه|ساعت|روز|هفته|ماه|سال)\s*پیش");
        if (!match.Success) return null;
        var n = match.Groups["n"].Value == "یک" ? 1 : int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        if (n > 10000) return null;
        return match.Groups["unit"].Value switch {
            "دقیقه" => fetchedAt.AddMinutes(-n), "ساعت" => fetchedAt.AddHours(-n), "روز" => fetchedAt.AddDays(-n),
            "هفته" => fetchedAt.AddDays(-7 * n), "ماه" => fetchedAt.AddDays(-31 * n), _ => fetchedAt.AddDays(-366 * n) };
    }
    public static string Normalize(string text)
    {
        var b = new StringBuilder();
        foreach (var c in HtmlEntity.DeEntitize(text).Normalize(NormalizationForm.FormKC))
            b.Append(c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c switch { 'ي' or 'ى' => 'ی', 'ك' => 'ک', '\u200c' => ' ', _ => char.ToLowerInvariant(c) });
        return Regex.Replace(b.ToString(), @"\s+", " ").Replace("دویست و هفت", "207").Trim();
    }
    public static MarketPriceResult Match(string query, IReadOnlyList<Entry> entries, DateTime now, int ageDays)
    {
        query = Normalize(query);
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = entries.Where(x => words.All(w => Regex.IsMatch(Normalize(x.Name), @"(?<![\p{L}\p{N}])" + Regex.Escape(w) + @"(?![\p{L}\p{N}])"))).ToArray();
        if (matches.Length == 0) return new("NotFound", FetchedAt: now);
        // Never choose by price proximity: the ad itself might be a down payment.
        if (matches.Select(x => (Normalize(x.Name), x.Price)).Distinct().Count() > 1) return new("Ambiguous", FetchedAt: now, Reason: "MultipleReferenceVariants");
        var entry = matches[0];
        if (entry.Price is null) return new("NotFound", entry.Name, FetchedAt: now, Url: entry.Url, Reason: "MarketPriceMissing");
        if (entry.PriceAt is null || entry.PriceAt < now.AddDays(-ageDays)) return new("Stale", entry.Name, entry.Price, entry.PriceAt, now, entry.Url, "UnknownOrOldSourceDate");
        return new("Matched", entry.Name, entry.Price, entry.PriceAt, now, entry.Url);
    }
}
