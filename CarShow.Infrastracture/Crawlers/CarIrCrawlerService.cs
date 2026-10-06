using System.Globalization;
using System.Text.RegularExpressions;
using CarShow.Domain.Crawling;
using Microsoft.Extensions.Logging;
using CarShow.Infrastracture.Configuration;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace CarShow.Infrastracture.Crawlers
{
    public sealed class CarIrCrawlerService : ICarIrCrawlerService, CarShow.Domain.Crawling.ICarDataSource
    {
        private readonly HttpClient _httpClient;
        private readonly CrawlerSettings _settings;
                private readonly ILogger<CarIrCrawlerService> _logger;

        public CarIrCrawlerService(HttpClient httpClient, IOptions<CrawlerSettings> options,
            ILogger<CarIrCrawlerService> logger)
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
                .GroupBy(x => $"{x.SourceUrl}|{x.CarName}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();

            var detailCache = new Dictionary<string, CarSourceDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(row.SourceUrl))
                {
                    if (!detailCache.TryGetValue(row.SourceUrl, out var detail))
                    {
                        detail = await ReadDetailsAsync(row.SourceUrl, cancellationToken);
                        detailCache[row.SourceUrl] = detail;
                    }

                    row.Details = detail;
                    row.ModelName = string.IsNullOrWhiteSpace(detail.Title) ? ExtractModelName(row.CarName) : detail.Title;
                    // Only an explicit label/value field is treated as a tip.
                    row.TipName = detail.Sections.SelectMany(x => x.Specifications)
                        .FirstOrDefault(x => x.Name is "تیپ" or "نام تیپ" or "نوع تیپ")?.Value;
                }


            }

            return rows;
        }

        private async Task<CarSourceDetails> ReadDetailsAsync(string url, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var document = new HtmlDocument();
            document.LoadHtml(await response.Content.ReadAsStringAsync(ct));
            return ParseDetails(document, url);
        }

        internal static CarSourceDetails ParseDetails(HtmlDocument document, string url)
        {
            var root = document.DocumentNode;
            var title = root.SelectSingleNode("//h1")
                ?? throw new InvalidDataException("Car detail page has no vehicle title.");
            // Parse content after the vehicle heading and before unrelated page sections.
            var boundary = root.Descendants().FirstOrDefault(x =>
                x.StreamPosition > title.StreamPosition && (x.Name == "footer" ||
                ((x.Name == "h2" || x.Name == "h3") &&
                 Regex.IsMatch(Normalize(x.InnerText), "نظرات کاربران|اخبار مرتبط|خودروهای مرتبط|خودروهای هم.رده"))));
            var nodes = root.Descendants().Where(x =>
                x.StreamPosition > title.StreamPosition &&
                (boundary == null || x.StreamPosition < boundary.StreamPosition)).ToList();

            var result = new CarSourceDetails { Title = Normalize(title.InnerText) };
            var section = new CarDetailSection { Name = "اطلاعات خودرو" };
            result.Sections.Add(section);
            foreach (var node in nodes)
            {
                if (node.Name is "h2" or "h3" or "h4")
                {
                    section = new CarDetailSection { Name = Normalize(node.InnerText) };
                    result.Sections.Add(section);
                }
                else if (node.Name == "tr")
                {
                    var cells = node.SelectNodes("./td|./th");
                    if (cells?.Count >= 2)
                    {
                        var key = Normalize(cells[0].InnerText);
                        var value = Normalize(string.Join(" ", cells.Skip(1).Select(x => x.InnerText)));
                        if (key.Length > 0 && value.Length > 0)
                            section.Specifications.Add(new CarSpecification { Name = key, Value = value });
                    }
                }
                else if (node.Name == "dt")
                {
                    var value = node.SelectSingleNode("following-sibling::dd[1]");
                    if (value != null)
                        section.Specifications.Add(new CarSpecification
                        { Name = Normalize(node.InnerText), Value = Normalize(value.InnerText) });
                }
                else if (node.Name is "p" or "li")
                {
                    var text = Normalize(node.InnerText);
                    if (text.Length > 0 && !node.Ancestors("nav").Any())
                        section.Paragraphs.Add(text);
                }
                else if (node.Name == "img")
                {
                    var alt = Normalize(node.GetAttributeValue("alt", ""));
                    // Avoid flag, author and avatar images.
                    if (alt.Length > 0 && !alt.Contains(result.Title) &&
                        !alt.Contains("Car.ir", StringComparison.OrdinalIgnoreCase)) continue;
                    var href = node.GetAttributeValue("data-src", node.GetAttributeValue("src", ""));
                    if (TryImageUrl(href, url, out var image)) result.Images.Add(image);
                }
            }
            var ogImage = root.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "");
            if (TryImageUrl(ogImage, url, out var primary)) result.Images.Insert(0, primary);
            result.Images = result.Images.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.ImageUrl = result.Images.FirstOrDefault() ?? "";
            result.Description = string.Join("\\n", result.Sections.SelectMany(x => x.Paragraphs));
            result.Sections.RemoveAll(x => x.Specifications.Count == 0 && x.Paragraphs.Count == 0);
            return result;
        }

        private static bool TryImageUrl(string? href, string source, out string image)
        {
            image = "";
            if (string.IsNullOrWhiteSpace(href)) return false;
            var absolute = MakeAbsoluteUrl(href, source);
            if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http")) return false;
            image = absolute;
            return true;
        }


