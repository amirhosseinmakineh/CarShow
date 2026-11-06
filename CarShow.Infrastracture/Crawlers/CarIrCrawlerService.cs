using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Crawling;
using CarShow.Infrastracture.Configuration;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarShow.Infrastracture.Crawlers;

public sealed class CarIrCrawlerService : ICarIrCrawlerService
{
    private readonly HttpClient _httpClient;
    private readonly CrawlerSettings _settings;
    private readonly ILogger<CarIrCrawlerService> _logger;

    public CarIrCrawlerService(HttpClient httpClient, IOptions<CrawlerSettings> options, ILogger<CarIrCrawlerService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CarSourceData>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var pricesUrl = CombineUrl(_settings.BaseUrl, _settings.PricesPath);
        using var response = await _httpClient.GetAsync(pricesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var rows = ParsePriceList(html, pricesUrl)
            .GroupBy(x => $@"{x.SourceUrl}|{x.CarName}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        if (rows.Count == 0)
            throw new InvalidDataException($"No car price rows were found at {pricesUrl}. Check the source response and selectors.");

        _logger.LogWarning("Car.ir price list parsed: {Count} rows from {Url}.", rows.Count, pricesUrl);
        // Prices are returned immediately. Details are enriched by the application layer afterwards.
        return rows;
    }

    public async Task<CarSourceDetails?> FetchDetailsAsync(string url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _httpClient.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();
        var document = new HtmlDocument();
        document.LoadHtml(await response.Content.ReadAsStringAsync(timeout.Token));
        return ParseDetails(document);
    }

    internal static List<CarSourceData> ParsePriceList(string html, string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var result = new List<CarSourceData>();

        foreach (var card in document.DocumentNode.SelectNodes("//div[contains(@class,'brand-price-list-item')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var company = Normalize(card.SelectSingleNode(".//*[contains(@class,'brand-header')]//*[contains(@class,'title')]")?.InnerText ?? string.Empty);
            foreach (var row in card.SelectNodes(".//tbody/tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var link = row.SelectSingleNode(".//a[contains(@href,'/')]");
                if (link is null) continue;
                var nameParts = link.SelectNodes(".//span")?.Select(x => Normalize(x.InnerText)).Where(x => x.Length > 0).ToList()
                    ?? new List<string>();
                var carName = nameParts.Count > 0 ? string.Join(" ", nameParts) : Normalize(link.InnerText);
                var cells = row.SelectNodes("./td");
                if (cells is null || cells.Count < 3) continue;

                result.Add(new CarSourceData
                {
                    CompanyName = company,
                    CarName = carName,
                    ModelName = ExtractModelName(carName),
                    FactoryPrice = ParsePrice(cells[1].InnerText),
                    MarketPrice = ParsePrice(cells[2].InnerText),
                    SourceUrl = MakeAbsoluteUrl(link.GetAttributeValue("href", string.Empty), sourceUrl)
                });
            }
        }

        return result;
    }

    internal static CarSourceDetails ParseDetails(HtmlDocument document)
    {
        var root = document.DocumentNode;
        var title = root.SelectSingleNode("//h1") ?? root.SelectSingleNode("//title");
        if (title is null) throw new InvalidDataException("Car detail page has no vehicle title.");

        var result = new CarSourceDetails { Title = Normalize(title.InnerText) };
        var current = new CarDetailSection { Name = "اطلاعات خودرو" };
        result.Sections.Add(current);

        foreach (var node in root.SelectNodes("//body//*[self::h2 or self::h3 or self::h4 or self::tr or self::p or self::li or self::img]") ?? Enumerable.Empty<HtmlNode>())
        {
            if (node.Ancestors("nav").Any() || node.Ancestors("footer").Any())
                continue;

            if (node.Name is "h2" or "h3" or "h4")
            {
                current = new CarDetailSection { Name = Normalize(node.InnerText) };
                result.Sections.Add(current);
                continue;
            }

            if (node.Name == "tr")
            {
                var cells = node.SelectNodes("./td|./th");
                if (cells?.Count >= 2)
                {
                    var key = Normalize(cells[0].InnerText);
                    var value = Normalize(string.Join(" ", cells.Skip(1).Select(x => x.InnerText)));
                    if (key.Length > 0 && value.Length > 0)
                        current.Specifications.Add(new CarSpecification { Name = key, Value = value });
                }
                continue;
            }

            if (node.Name is "p" or "li")
            {
                var text = Normalize(node.InnerText);
                if (text.Length > 0 && !node.Ancestors("nav").Any() && !current.Paragraphs.Contains(text))
                    current.Paragraphs.Add(text);
                continue;
            }

            var href = node.GetAttributeValue("data-src", node.GetAttributeValue("src", string.Empty));
            if (TryImageUrl(href, out var image) && !result.Images.Contains(image, StringComparer.OrdinalIgnoreCase))
                result.Images.Add(image);
        }

        var ogImage = root.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", string.Empty);
        if (TryImageUrl(ogImage, out var primary))
            result.Images.Insert(0, primary);

        result.Images = result.Images.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.ImageUrl = result.Images.FirstOrDefault() ?? string.Empty;
        result.Description = string.Join("\n", result.Sections.SelectMany(x => x.Paragraphs).Distinct());
        result.Sections.RemoveAll(x => x.Specifications.Count == 0 && x.Paragraphs.Count == 0);
        return result;

        bool TryImageUrl(string? href, out string image)
        {
            image = string.Empty;
            if (string.IsNullOrWhiteSpace(href)) return false;
            if (!Uri.TryCreate(MakeAbsoluteUrl(href, "https://car.ir"), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https")) return false;
            image = uri.ToString();
            return true;
        }
    }

    private static decimal ParsePrice(string text)
    {
        var normalized = NormalizeDigits(text).Replace(",", string.Empty).Replace("٬", string.Empty);
        var match = Regex.Match(normalized, @"\d+");
        return match.Success && decimal.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    private static string ExtractModelName(string name)
    {
        var value = Regex.Replace(name, @"\s+مدل\s+\d{4}", string.Empty, RegexOptions.IgnoreCase).Trim();
        return value.Length == 0 ? name : value;
    }

    private static string CombineUrl(string baseUrl, string path)
        => MakeAbsoluteUrl(path, baseUrl);

    private static string MakeAbsoluteUrl(string href, string source)
    {
        if (string.IsNullOrWhiteSpace(href))
            throw new InvalidDataException("The source URL is empty.");

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
            return absolute.ToString();

        if (!Uri.TryCreate(source, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Crawler BaseUrl must be an absolute HTTP or HTTPS URL.");

        var resolved = new Uri(baseUri, href);
        if (resolved.Scheme is not ("http" or "https"))
            throw new InvalidDataException("The resolved source URL must use HTTP or HTTPS.");
        return resolved.ToString();
    }

    private static string Normalize(string value)
        => Regex.Replace(NormalizeDigits(HtmlEntity.DeEntitize(value ?? string.Empty)), @"\s+", " ").Trim();

    private static string NormalizeDigits(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + c - '۰'),
                >= '٠' and <= '٩' => (char)('0' + c - '٠'),
                _ => c
            });
        return sb.ToString();
    }
}
