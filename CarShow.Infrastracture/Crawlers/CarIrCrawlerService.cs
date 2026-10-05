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

        public CarIrCrawlerService(
            HttpClient httpClient,
            IOptions<CrawlerSettings> options,
            CarShowContext db,
            ILogger<CarIrCrawlerService> logger)
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
            var cars = Parse(html, pricesUrl)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.DetailUrl)
                    ? $"{x.CompanyName}|{x.CarName}"
                    : x.DetailUrl, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();

            var synced = 0;
            foreach (var dto in cars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpsertAsync(dto, cancellationToken);
                synced++;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Car.ir crawler synchronized {Count} cars.", synced);
            return synced;
        }

        private async Task UpsertAsync(CarPriceCrawlerDto dto, CancellationToken cancellationToken)
        {
            var companyName = Normalize(dto.CompanyName);
            var carName = Normalize(dto.CarName);
            if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(carName))
                return;

            var company = await _db.Companies.FirstOrDefaultAsync(
                x => x.Name == companyName, cancellationToken);

            if (company is null)
            {
                company = new Company { Name = companyName };
                _db.Companies.Add(company);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var modelName = ExtractModelName(carName);
            var carModel = await _db.CarModels.FirstOrDefaultAsync(
                x => x.Name == modelName, cancellationToken);

            if (carModel is null)
            {
                carModel = new CarModel { Name = modelName };
                _db.CarModels.Add(carModel);
                await _db.SaveChangesAsync(cancellationToken);
            }

            Tip? tip = null;
            var tipName = Normalize(dto.TipName);
            if (!string.IsNullOrWhiteSpace(tipName))
            {
                tip = await _db.Tips.FirstOrDefaultAsync(
                    x => x.Name == tipName, cancellationToken);

                if (tip is null)
                {
                    tip = new Tip { Name = tipName };
                    _db.Tips.Add(tip);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            var existing = await _db.Cars.FirstOrDefaultAsync(
                x => x.CompanyId == company.Id
                    && x.CarModelId == carModel.Id
                    && x.TipId == (tip == null ? null : tip.Id),
                cancellationToken);

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                existing = new Car
                {
                    CompanyId = company.Id,
                    CarModelId = carModel.Id,
                    TipId = tip?.Id,
                    Name = carName,
                    Slug = CreateSlug(dto.DetailUrl, carName),
                    SourceUrl = dto.DetailUrl,
                    LastUpdated = now,
                    MarketPrice = dto.MarketPrice,
                    FactoryPrice = dto.FactoryPrice
                };

                _db.Cars.Add(existing);
                _db.CarPriceHistories.Add(new CarPriceHistory
                {
                    Car = existing,
                    MarketPrice = dto.MarketPrice,
                    FactoryPrice = dto.FactoryPrice,
                    Date = now
                });
                return;
            }

            var priceChanged = existing.MarketPrice != dto.MarketPrice
                || existing.FactoryPrice != dto.FactoryPrice;

            existing.Name = carName;
            existing.SourceUrl = dto.DetailUrl;
            existing.Slug = CreateSlug(dto.DetailUrl, carName);
            existing.LastUpdated = now;
            existing.MarketPrice = dto.MarketPrice;
            existing.FactoryPrice = dto.FactoryPrice;

            if (priceChanged)
            {
                _db.CarPriceHistories.Add(new CarPriceHistory
                {
                    CarId = existing.Id,
                    MarketPrice = dto.MarketPrice,
                    FactoryPrice = dto.FactoryPrice,
                    Date = now
                });
            }
        }

        private static IReadOnlyList<CarPriceCrawlerDto> Parse(string html, string sourceUrl)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var rows = document.DocumentNode.SelectNodes("//table//tr[td]");
            if (rows is null)
                return Array.Empty<CarPriceCrawlerDto>();

            var headings = document.DocumentNode.SelectNodes("//h2|//h3")
                ?.ToList() ?? new List<HtmlNode>();
            var result = new List<CarPriceCrawlerDto>();

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./td");
                if (cells is null || cells.Count < 3)
                    continue;

                var anchor = cells[0].SelectSingleNode(".//a[@href]");
                var carName = Normalize(cells[0].InnerText);
                if (anchor is null || string.IsNullOrWhiteSpace(carName))
                    continue;

                var companyName = FindNearestCompany(row, headings);
                if (string.IsNullOrWhiteSpace(companyName))
                    continue;

                result.Add(new CarPriceCrawlerDto
                {
                    CompanyName = companyName,
                    CarName = carName,
                    FactoryPrice = ParsePrice(cells[1].InnerText),
                    MarketPrice = ParsePrice(cells[2].InnerText),
                    DetailUrl = MakeAbsoluteUrl(anchor.GetAttributeValue("href", string.Empty), sourceUrl)
                });
            }

            return result;
        }

        private static string FindNearestCompany(HtmlNode row, IReadOnlyList<HtmlNode> headings)
        {
            var rowIndex = row.StreamPosition;
            var heading = headings
                .Where(x => x.StreamPosition < rowIndex)
                .OrderByDescending(x => x.StreamPosition)
                .FirstOrDefault();

            return NormalizeCompany(heading?.InnerText ?? string.Empty);
        }

        private static decimal ParsePrice(string value)
        {
            var normalized = Normalize(value)
                .Replace("تومان", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(",", string.Empty)
                .Replace("٬", string.Empty)
                .Trim();

            if (normalized is "" or "---" or "-")
                return 0m;

            normalized = ToEnglishDigits(normalized);
            var numeric = Regex.Replace(normalized, @"[^0-9.]", string.Empty);
            return decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                ? price
                : 0m;
        }

        private static string ExtractModelName(string carName)
        {
            var model = Regex.Replace(carName, @"\s*مدل\s*\d{4}", string.Empty);
            model = Regex.Replace(model, @"^ماشین\s+", string.Empty);
            return Normalize(model);
        }

        private static string CreateSlug(string detailUrl, string carName)
        {
            if (Uri.TryCreate(detailUrl, UriKind.Absolute, out var uri))
            {
                var segment = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
                if (!string.IsNullOrWhiteSpace(segment))
                    return segment;
            }

            return Regex.Replace(carName.Trim().ToLowerInvariant(), @"\s+", "-");
        }

        private static string CombineUrl(string baseUrl, string path)
            => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        private static string MakeAbsoluteUrl(string href, string sourceUrl)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
                && Uri.TryCreate(source, href, out var relative))
                return relative.ToString();

            return href;
        }

        private static string NormalizeCompany(string value)
            => Normalize(value)
                .Replace("قیمت محصولات", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("محصولات", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

        private static string Normalize(string value)
            => ToEnglishDigits(HtmlEntity.DeEntitize(value ?? string.Empty))
                .Replace("\u200c", " ")
                .Replace("\u00a0", " ")
                .Trim();

        private static string ToEnglishDigits(string value)
        {
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    '۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4',
                    '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9',
                    '٠' => '0', '١' => '1', '٢' => '2', '٣' => '3', '٤' => '4',
                    '٥' => '5', '٦' => '6', '٧' => '7', '٨' => '8', '٩' => '9',
                    _ => chars[i]
                };
            }

            return new string(chars);
        }
    }
}
