using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CarShow.Infrastracture.Telegram;

public sealed record TelegramCar(string Name, int Year, decimal Price, string Channel, long MessageId, string MessageUrl);
public sealed record TelegramChannelScan(string Channel, int MessagesProcessed, int CarsFound, string? Error);
public sealed record TelegramScanResult(IReadOnlyList<TelegramCar> Cars, IReadOnlyList<TelegramChannelScan> Channels);

public sealed partial class TelegramCarService(HttpClient httpClient)
{
    // Public usernames from the owner's approved list. The private invite cannot be read via /s/.
    public static readonly IReadOnlyList<string> Channels = [
        "autopalermo", "davoodkelasii", "tajcarsefr", "autozamanzade", "miladforoud",
        "autoland41", "haminkhodroo", "adib_khodro_qom", "gallery20moradi", "car_sales_gandomi",
        "autosolhi", "behzadsaberi_oxinkhodro", "auto_ap", "avatttgalry", "tehrankhodro97",
        "mohamadrezamansoor", "payamkhodrooo", "auto_teymouri", "shahabbesharaty", "autoesmieh",
        "omidabbasicarsale", "otoeiliya", "autogalerymhmood", "kharid0forosh0mashin", "eshraghicar",
        "irankodroohadiali", "namayeshgahdarantehran21", "hashemi_car2", "bazarkhodroh",
        "modern_khodrooooo", "thehhrankhodrow", "autoplack", "automarkethamidnasiri",
        "goroohmashin", "khodro_mashin_tehran_divar_bama", "otosefr_farzadakbari",
        "rafieicar", "hhirsa", "wolfkhodro", "sefidgarali", "aradkhodro96", "payamrahnama",
        "uotobehroz", "autoluxy", "otoadelkhani", "autogorgangolbini", "ehsaaant",
        "autoramin9787", "automeysamsaghi", "auto_dayy", "shakiba_iran_16",
        "hoseinbabaieqom", "auto_royal_meshkin", "auto_najafiqom", "mehrdadloghmanii",
        "alavicarexhibition", "mamatir_khodro_hosseini", "grohcobrotehran", "khazarrkhodro",
        "auto_1abdolmaleki1", "autopasdaran", "hakimghailarrr", "auto_yektaa",
        "jamshidsamadiiii", "auto_jahan", "ilam98khodro", "autosardarr", "khodro_nasri",
        "autovalayati", "outorashidiyan", "sahracar", "arasmehri", "parsiankhodroqolizade",
        "kiu2016", "arsinkhodro", "bakshabadi", "automojtaba66", "autoeightasgarinia",
        "keyvancar", "pejman2616", "auto_tabrizi", "hesamfatemikaraj", "autofezolahi",
        "irankhodromonirikashan"
    ];

    private static readonly Regex YearRegex = new(@"(?<!\d)(?:13\d{2}|14\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex LabeledYearRegex = new(@"(?:مدل|سال(?:\s*ساخت)?|year)\s*[:：]?\s*(?<year>13\d{2}|14\d{2}|20\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PriceRegex = new(@"(?<number>\d{1,3}(?:[,٬.]\d{3})+|\d+(?:[.٫]\d+)?)\s*(?<unit>میلیارد|میلیون|هزار|م(?![\p{L}\p{N}]))?(?:\s*و\s*(?<extra>\d[\d,٬.]*)\s*(?<extraUnit>میلیون|هزار))?\s*(?<currency>تومان|تومن|ریال|IRT|IRR)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PriceLabelRegex = new(@"(?:قیمت(?:\s+(?:فروش|نقدی|کل))?|نقدی|بها)\s*[:：=\-💰💵]*\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<TelegramScanResult> GetCarsAsync(int maxMessagesPerChannel = 20, string? channel = null, CancellationToken cancellationToken = default)
    {
        if (maxMessagesPerChannel is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maxMessagesPerChannel), "Choose 1 through 500 messages per channel.");
        var selected = channel is null ? Channels : Channels.Where(c => c.Equals(channel.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Count == 0)
            throw new ArgumentException("Channel is not in the approved public list.", nameof(channel));

        var cars = new ConcurrentBag<TelegramCar>();
        var scans = new ConcurrentBag<TelegramChannelScan>();
        await Parallel.ForEachAsync(selected, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (name, ct) =>
        {
            var count = 0;
            var found = 0;
            string? error = null;
            long? before = null;
            var seenIds = new HashSet<long>();
            try
            {
                while (count < maxMessagesPerChannel)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://t.me/s/{name}" + (before is null ? "" : $"?before={before}"));
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TelegramCarScanner/1.0)");
                    using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode) { error = $"HTTP {(int)response.StatusCode}"; break; }
                    var document = new HtmlDocument();
                    var html = await response.Content.ReadAsStringAsync(ct);
                    if (html.Contains("<title>Site Unavailable</title>", StringComparison.OrdinalIgnoreCase))
                    { error = "Telegram page unavailable from this network"; break; }
                    document.LoadHtml(html);
                    var nodes = document.DocumentNode.SelectNodes("//div[@data-post and contains(concat(' ', normalize-space(@class), ' '), ' tgme_widget_message ')]");
                    if (nodes is null || nodes.Count == 0) { error = "No accessible public messages"; break; }
                    var messages = nodes.Select(n => (Node: n, Id: ReadMessageId(n.GetAttributeValue("data-post", ""))))
                        .Where(x => x.Id > 0 && (before is null || x.Id < before)).OrderByDescending(x => x.Id).ToArray();
                    if (messages.Length == 0) break;
                    var added = 0;
                    foreach (var (node, id) in messages)
                    {
                        if (count >= maxMessagesPerChannel) break;
                        if (!seenIds.Add(id)) continue;
                        count++; added++;
                        var textNode = node.SelectSingleNode(".//*[contains(concat(' ', normalize-space(@class), ' '), ' tgme_widget_message_text ')]");
                        if (textNode is null) continue;
                        foreach (var br in textNode.Descendants("br").ToList()) br.ParentNode?.ReplaceChild(document.CreateTextNode("\n"), br);
                        var text = HtmlEntity.DeEntitize(textNode.InnerText).Trim();
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var seenInMessage = new HashSet<string>();
                        foreach (var car in ExtractCars(text))
                        {
                            if (!seenInMessage.Add($"{car.Name}|{car.Year}|{car.Price}")) continue;
                            cars.Add(new TelegramCar(car.Name, car.Year, car.Price, name, id, $"https://t.me/{name}/{id}"));
                            found++;
                        }
                    }
                    if (added == 0) break;
                    var oldest = messages.Min(x => x.Id);
                    if (before is not null && oldest >= before) break;
                    before = oldest;
                    if (count < maxMessagesPerChannel) await Task.Delay(800, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { error = ex is HttpRequestException ? "Telegram request failed" : ex is TaskCanceledException ? "Telegram timeout" : "Telegram response could not be processed"; }
            scans.Add(new TelegramChannelScan(name, count, found, error));
        });
        return new TelegramScanResult(cars.OrderBy(c => c.Channel).ThenByDescending(c => c.MessageId).ToArray(), scans.OrderBy(s => s.Channel).ToArray());
    }

    private static long ReadMessageId(string value) => long.TryParse(value[(value.LastIndexOf('/') + 1)..], out var id) ? id : 0;

    public static IReadOnlyList<(string Name, int Year, decimal Price)> ExtractCars(string message)
    {
        var result = new List<(string Name, int Year, decimal Price)>();
        var block = new List<string>();
        foreach (var rawLine in Regex.Split(Normalize(message), @"[\r\n]+"))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (Regex.IsMatch(line, @"(?:قسط|اقساط|پیش\s*پرداخت|بیعانه|اجاره)") && !Regex.IsMatch(line, @"قیمت\s*نقدی")) continue;
            block.Add(line);
            var label = PriceLabelRegex.Match(line);
            var pricePart = label.Success ? line[(label.Index + label.Length)..] : line;
            var priceMatch = PriceRegex.Match(pricePart);
            if (!priceMatch.Success)
            {
                if (block.Count > 8) block.RemoveAt(0);
                continue;
            }
            var startsWithPrice = Regex.IsMatch(line, @"^\s*\d[\d,٬.]*\s*(?:میلیارد|میلیون|م(?![\p{L}\p{N}])|تومان|ریال)");
            if (!label.Success && !startsWithPrice)
            {
                if (block.Count > 8) block.RemoveAt(0);
                continue;
            }
            if (!priceMatch.Groups["unit"].Success && !priceMatch.Groups["currency"].Success && !label.Success) continue;
            var candidate = string.Join("\n", block);
            block.Clear();
            if (Regex.IsMatch(candidate, @"توافقی|قیمت\s*در\s*دایرکت")) continue;
            var years = LabeledYearRegex.Matches(candidate).Select(x => x.Groups["year"].Value).Distinct().ToArray();
            if (years.Length == 0) years = YearRegex.Matches(candidate).Select(x => x.Value).Distinct().ToArray();
            if (years.Length != 1 || !int.TryParse(years[0], out var year) || !TryParsePrice(priceMatch, out var price)) continue;
            var lineStart = candidate.LastIndexOf(line, StringComparison.Ordinal);
            if (lineStart < 0) continue;
            var priceStart = lineStart + (label.Success ? label.Index + label.Length : 0) + priceMatch.Index;
            var nameText = PriceLabelRegex.Replace(YearRegex.Replace(LabeledYearRegex.Replace(candidate[..priceStart], " "), " "), " ");
            var name = nameText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(CleanName)
                .LastOrDefault(x => x.Length >= 2 && Regex.IsMatch(x, @"[\p{L}]") && !Regex.IsMatch(x, @"^(?:مدل|سال|قیمت|کارکرد|بیمه|تماس|تلفن|موبایل)\b"));
            if (name is not null) result.Add((name, year, price));
        }
        return result;
    }

    private static bool TryParsePrice(Match match, out decimal price)
    {
        price = 0;
        if (!TryNumber(match.Groups["number"].Value, out var number)) return false;
        var multiplier = match.Groups["unit"].Value switch { "میلیارد" => 1_000_000_000m, "میلیون" or "م" => 1_000_000m, "هزار" => 1_000m, _ => 1m };
        if (multiplier == 1m && number < 10_000_000m) return false;
        price = number * multiplier;
        if (match.Groups["extra"].Success)
        {
            if (!TryNumber(match.Groups["extra"].Value, out var extra)) return false;
            price += extra * (match.Groups["extraUnit"].Value == "میلیون" ? 1_000_000m : 1_000m);
        }
        if (match.Groups["currency"].Value.Equals("ریال", StringComparison.OrdinalIgnoreCase) ||
            match.Groups["currency"].Value.Equals("IRR", StringComparison.OrdinalIgnoreCase)) price /= 10m;
        return price is >= 10_000_000m and <= 100_000_000_000m;
    }

    private static bool TryNumber(string input, out decimal number)
    {
        input = input.Replace(",", "").Replace("٬", "").Replace("٫", ".");
        if (Regex.IsMatch(input, @"^\d{1,3}\.\d{3}$")) input = input.Replace(".", "");
        return decimal.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }

    private static string CleanName(string input)
    {
        input = Regex.Replace(input, @"^(?:فروش(?:\s+فوری)?|نام\s*خودرو|خودرو)\s*[:：\-]?\s*", "", RegexOptions.IgnoreCase);
        input = Regex.Replace(input, @"\s+(?:کارکرد|کیلومتر|بیمه|شماره\s*تماس|تلفن)\s*[:：]?.*$", "", RegexOptions.IgnoreCase);
        input = Regex.Replace(input, @"[^\p{L}\p{N}\s+\-]", " ");
        return Regex.Replace(input, @"\s+", " ").Trim();
    }

    private static string Normalize(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(character);
            builder.Append(digit >= 0 ? (char)('0' + digit) : character);
        }
        return builder.ToString().Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ');
    }
}
