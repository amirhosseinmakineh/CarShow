using System.Globalization;
using System.Text.RegularExpressions;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Configuration;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Crawlers.DTOs;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarShow.Infrastracture.Crawlers
{
    public sealed class CarIrCrawlerService : ICarIrCrawlerService
    {
        private readonly HttpClient _httpClient;
        private readonly CrawlerSettings _settings;
        private readonly CarShowContext _db;
        private readonly ILogger<CarIrCrawlerService> _logger;

        public CarIrCrawlerService(HttpClient httpClient, IOptions<CrawlerSettings> options,
            CarShowContext db, ILogger<CarIrCrawlerService> logger)
        {
            _httpClient = httpClient;
            _settings = options.Value;
            _db = db;
            _logger = logger;
        }

        public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
        {
            var pricesUrl = CombineUrl(_settings.BaseUrl, _settings.PricesPath);
            using var response = await _httpClient.GetAsync(pricesUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var rows = ParsePriceList(html, pricesUrl)
                .GroupBy(x => $"{x.DetailUrl}|{x.CarName}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();

            var detailCache = new Dictionary<string, DetailInfo>(StringComparer.OrdinalIgnoreCase);
            var synced = 0;

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(row.DetailUrl))
                {
                    if (!detailCache.TryGetValue(row.DetailUrl, out var detail))
                    {
                        detail = await ReadDetailsAsync(row.DetailUrl, cancellationToken);
                        detailCache[row.DetailUrl] = detail;
                    }

                    row.ImageUrl = detail.ImageUrl;
                    row.Description = detail.Description;
                    if (!string.IsNullOrWhiteSpace(detail.TipName))
                        row.TipName = detail.TipName;
                }

                await UpsertAsync(row, cancellationToken);
                synced++;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Car.ir crawler synchronized {Count} cars.", synced);
            return synced;
        }

        private async Task UpsertAsync(CarPriceCrawlerDto dto, CancellationToken cancellationToken)
        {
            var companyName = NormalizeCompany(dto.CompanyName);
            var carName = Normalize(dto.CarName);
            if (companyName.Length == 0 || carName.Length == 0) return;

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Name == companyName, cancellationToken);
            if (company is null)
            {
                company = new Company { Name = companyName };
                _db.Companies.Add(company);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var modelName = ExtractModelName(carName);
            var model = await _db.CarModels.FirstOrDefaultAsync(x => x.Name == modelName, cancellationToken);
            if (model is null)
            {
                model = new CarModel { Name = modelName };
                _db.CarModels.Add(model);
                await _db.SaveChangesAsync(cancellationToken);
            }

            Tip? tip = null;
            var tipName = Normalize(dto.TipName);
            if (tipName.Length > 0)
            {
                tip = await _db.Tips.FirstOrDefaultAsync(x => x.Name == tipName, cancellationToken);
                if (tip is null)
                {
                    tip = new Tip { Name = tipName };
                    _db.Tips.Add(tip);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            var existing = await _db.Cars.FirstOrDefaultAsync(x =>
                x.SourceUrl == dto.DetailUrl && x.Name == carName, cancellationToken);

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                existing = new Car
                {
                    CompanyId = company.Id, CarModelId = model.Id, TipId = tip?.Id,
                    Name = carName, Slug = CreateSlug(dto.DetailUrl, carName),
                    ImageName = dto.ImageUrl, Description = dto.Description,
                    SourceUrl = dto.DetailUrl, LastUpdated = now,
                    MarketPrice = dto.MarketPrice, FactoryPrice = dto.FactoryPrice
                };
                _db.Cars.Add(existing);
                _db.CarPriceHistories.Add(new CarPriceHistory
                {
                    Car = existing, MarketPrice = dto.MarketPrice,
                    FactoryPrice = dto.FactoryPrice, Date = now
                });
                return;
            }

            var priceChanged = existing.MarketPrice != dto.MarketPrice || existing.FactoryPrice != dto.FactoryPrice;
            existing.CompanyId = company.Id;
            existing.CarModelId = model.Id;
            existing.TipId = tip?.Id;
            existing.Name = carName;
            existing.ImageName = string.IsNullOrWhiteSpace(dto.ImageUrl) ? existing.ImageName : dto.ImageUrl;
            existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? existing.Description : dto.Description;
            existing.Slug = CreateSlug(dto.DetailUrl, carName);
            existing.SourceUrl = dto.DetailUrl;
            existing.LastUpdated = now;
            existing.MarketPrice = dto.MarketPrice;
            existing.FactoryPrice = dto.FactoryPrice;

            if (priceChanged)
                _db.CarPriceHistories.Add(new CarPriceHistory
                {
                    CarId = existing.Id, MarketPrice = dto.MarketPrice,
                    FactoryPrice = dto.FactoryPrice, Date = now
                });
        }

        private async Task<DetailInfo> ReadDetailsAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return new DetailInfo();

                var document = new HtmlDocument();
                document.LoadHtml(await response.Content.ReadAsStringAsync(ct));

                var name = Normalize(document.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? "");
                var image = document.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                    ?.GetAttributeValue("content", "") ?? "";
                if (image.Length == 0)
                    image = document.DocumentNode.SelectSingleNode("//main//img[@src]")?.GetAttributeValue("src", "") ?? "";
                image = MakeAbsoluteUrl(image, url);

                var description = Normalize(document.DocumentNode.SelectSingleNode("//meta[@name='description']")
                    ?.GetAttributeValue("content", "") ?? "");
                if (description.Length == 0)
                {
                    var paragraphs = document.DocumentNode.SelectNodes("//main//p")
                        ?.Select(x => Normalize(x.InnerText))
                        .Where(x => x.Length > 30)
                        .Take(20) ?? Enumerable.Empty<string>();
                    description = string.Join(" ", paragraphs);
                }

                var tip = Normalize(document.DocumentNode.SelectSingleNode(
                    "//*[contains(normalize-space(text()), 'تیپ')]")?.InnerText ?? "");
                return new DetailInfo { Name = name, ImageUrl = image, Description = description, TipName = tip };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Could not read car detail page {Url}.", url);
                return new DetailInfo();
            }
        }

        private static IReadOnlyList<CarPriceCrawlerDto> ParsePriceList(string html, string sourceUrl)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);
            var rows = document.DocumentNode.SelectNodes("//table//tr[td]");
            var headings = document.DocumentNode.SelectNodes("//h2|//h3")?.ToList() ?? new();
            if (rows is null) return Array.Empty<CarPriceCrawlerDto>();

            var result = new List<CarPriceCrawlerDto>();
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./td");
                var anchor = cells?.Count >= 3 ? cells[0].SelectSingleNode(".//a[@href]") : null;
                if (cells is null || cells.Count < 3 || anchor is null) continue;

                var name = Normalize(cells[0].InnerText);
                var company = FindNearestCompany(row, headings);
                if (name.Length == 0 || company.Length == 0) continue;

                result.Add(new CarPriceCrawlerDto
                {
                    CompanyName = company,
                    CarName = name,
                    FactoryPrice = ParsePrice(cells[1].InnerText),
                    MarketPrice = ParsePrice(cells[2].InnerText),
                    DetailUrl = MakeAbsoluteUrl(anchor.GetAttributeValue("href", ""), sourceUrl)
                });
            }
            return result;
        }

        private static decimal ParsePrice(string value)
        {
            var text = ToEnglishDigits(Normalize(value));
            if (text.Contains("---", StringComparison.Ordinal) || text == "-") return 0m;
            var match = Regex.Match(text, @"([0-9][0-9,٬.]*)\s*تومان");
            if (!match.Success) return 0m;
            var number = match.Groups[1].Value.Replace(",", "").Replace("٬", "");
            return decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : 0m;
        }

        private static string FindNearestCompany(HtmlNode row, IReadOnlyList<HtmlNode> headings)
        {
            var heading = headings.Where(x => x.StreamPosition < row.StreamPosition)
                .OrderByDescending(x => x.StreamPosition).FirstOrDefault();
            return NormalizeCompany(heading?.InnerText ?? "");
        }

        private static string ExtractModelName(string name)
            => Normalize(Regex.Replace(Regex.Replace(name, @"\s*مدل\s*\d{4}", ""), @"^ماشین\s+", ""));

        private static string ExtractYear(string name)
            => Regex.Match(name, @"\b\d{4}\b").Value;

        private static string CreateSlug(string url, string name)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? "";
            return Regex.Replace(name.ToLowerInvariant(), @"\s+", "-");
        }

        private static string CombineUrl(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        private static string MakeAbsoluteUrl(string href, string source)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute)) return absolute.ToString();
            return Uri.TryCreate(source, UriKind.Absolute, out var root) && Uri.TryCreate(root, href, out var result)
                ? result.ToString() : href;
        }

        private static string NormalizeCompany(string value)
            => Normalize(value).Replace("قیمت محصولات", "").Replace("محصولات", "").Trim();

        private static string Normalize(string value)
            => ToEnglishDigits(HtmlEntity.DeEntitize(value ?? "")).Replace("\u200c", " ").Replace("\u00a0", " ").Trim();

        private static string ToEnglishDigits(string value)
        {
            return new string(value.Select(c => c switch
            {
                '۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4',
                '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9',
                '٠' => '0', '١' => '1', '٢' => '2', '٣' => '3', '٤' => '4',
                '٥' => '5', '٦' => '6', '٧' => '7', '٨' => '8', '٩' => '9',
                _ => c
            }).ToArray());
        }

        private sealed record DetailInfo(string Name = "", string ImageUrl = "", string Description = "", string TipName = "");
    }
}
