using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

public sealed record TelegramCar(
    string Name,
    int Year,
    decimal Price
);

public sealed class TelegramCarService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    private static readonly string[] Channels =
    [
        "autopalermo",
        "davoodkelasii",
        "tajcarsefr",
        "autozamanzade",
        "miladforoud",
        "autoland41",
        "haminkhodroo",
        "adib_khodro_qom",
        "gallery20moradi",
        "car_sales_gandomi",
        "autosolhi",
        "behzadsaberi_oxinkhodro",
        "auto_ap",
        "avatttgalry",
        "tehrankhodro97",
        "mohamadrezamansoor",
        "payamkhodrooo",
        "auto_teymouri",
        "shahabbesharaty",
        "autoesmieh",
        "omidabbasicarsale",
        "otoeiliya",
        "autogalerymhmood",
        "kharid0forosh0mashin",
        "eshraghicar",
        "irankodroohadiali",
        "namayeshgahdarantehran21",
        "hashemi_car2",
        "bazarkhodroh",
        "modern_khodrooooo",
        "thehhrankhodrow",
        "autoplack",
        "automarkethamidnasiri",
        "goroohmashin",
        "khodro_mashin_tehran_divar_bama",
        "otosefr_farzadakbari",
        "rafieicar",
        "hhirsa",
        "wolfkhodro",
        "sefidgarali",
        "aradkhodro96",
        "payamrahnama",
        "uotobehroz",
        "autoluxy",
        "otoadelkhani",
        "autogorgangolbini",
        "ehsaaant",
        "autoramin9787",
        "automeysamsaghi",
        "auto_dayy",
        "shakiba_iran_16",
        "hoseinbabaieqom",
        "auto_royal_meshkin",
        "auto_najafiqom",
        "mehrdadloghmanii",
        "alavicarexhibition",
        "mamatir_khodro_hosseini",
        "grohcobrotehran",
        "khazarrkhodro",
        "auto_1abdolmaleki1",
        "autopasdaran",
        "hakimghailarrr",
        "auto_yektaa",
        "jamshidsamadiiii",
        "auto_jahan",
        "ilam98khodro",
        "autosardarr",
        "khodro_nasri",
        "autovalayati",
        "outorashidiyan",
        "sahracar",
        "arasmehri",
        "parsiankhodroqolizade",
        "kiu2016",
        "arsinkhodro",
        "bakshabadi",
        "automojtaba66",
        "autoeightasgarinia",
        "keyvancar",
        "pejman2616",
        "auto_tabrizi",
        "hesamfatemikaraj",
        "autofezolahi",
        "irankhodromonirikashan"
    ];

    public static IReadOnlyList<string> ApprovedChannels { get; } = Array.AsReadOnly(Channels);

    private static readonly Regex YearRegex = new(
        @"(?<!\d)(?:13\d{2}|14\d{2}|20\d{2})(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex LabeledYearRegex = new(
        @"(?:مدل|سال(?:\s*ساخت)?|year)\s*[:：]?\s*" +
        @"(?<year>13\d{2}|14\d{2}|20\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PriceRegex = new(
        @"(?<number>\d{1,3}(?:[,٬.]\d{3})+|\d+(?:[.٫]\d+)?)" +
        @"\s*(?<unit>میلیارد|میلیون|هزار|م(?![\p{L}\p{N}]))?" +
        @"(?:\s*و\s*(?<extra>\d[\d,٬.]*)" +
        @"\s*(?<extraUnit>میلیون|هزار))?" +
        @"\s*(?<currency>تومان|تومن|ریال|IRT|IRR)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PriceLabelRegex = new(
        @"(?:قیمت(?:\s+(?:فروش|نقدی|کل))?|نقدی|بها)" +
        @"\s*[:：=\-💰💵]*\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TelegramCarService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();

        if (_ownsClient)
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<TelegramCar>> GetCarsAsync(
        int maxMessagesPerChannel = 500,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        string? selectedChannel = null)
    {
        if (maxMessagesPerChannel <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxMessagesPerChannel));

        if (selectedChannel is not null &&
            !Channels.Contains(selectedChannel, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Channel is not in the approved public list.", nameof(selectedChannel));

        var cars = new List<TelegramCar>();

        foreach (var channel in selectedChannel is null
            ? Channels
            : Channels.Where(x => x.Equals(selectedChannel, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var collectedMessages = 0;
            long? before = null;

            var seenMessageIds = new HashSet<long>();
            var seenCars = new HashSet<string>();

            try
            {
                while (collectedMessages < maxMessagesPerChannel)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var url = $"https://t.me/s/{channel}";

                    if (before.HasValue)
                        url += $"?before={before.Value}";

                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        url);

                    request.Headers.UserAgent.ParseAdd(
                        "Mozilla/5.0 (compatible; TelegramCarScanner/1.0)");

                    using var response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        log?.Invoke(
                            $"{channel}: HTTP {(int)response.StatusCode}");
                        break;
                    }

                    var html = await response.Content
                        .ReadAsStringAsync(cancellationToken);

                    var document = new HtmlDocument();
                    document.LoadHtml(html);

                    var nodes = document.DocumentNode.SelectNodes(
                        "//div[@data-post and " +
                        "contains(concat(' ', normalize-space(@class), ' '), " +
                        "' tgme_widget_message ')]");

                    if (nodes is null || nodes.Count == 0)
                    {
                        log?.Invoke(
                            $"{channel}: No accessible public messages.");
                        break;
                    }

                    var pageMessages = nodes
                        .Select(node => new
                        {
                            Node = node,
                            Id = ReadMessageId(
                                node.GetAttributeValue("data-post", ""))
                        })
                        .Where(x =>
                            x.Id > 0 &&
                            (!before.HasValue || x.Id < before.Value))
                        .OrderByDescending(x => x.Id)
                        .ToList();

                    if (pageMessages.Count == 0)
                        break;

                    var oldestId = pageMessages.Min(x => x.Id);
                    var newMessages = 0;

                    foreach (var message in pageMessages)
                    {
                        if (collectedMessages >= maxMessagesPerChannel)
                            break;

                        if (!seenMessageIds.Add(message.Id))
                            continue;

                        newMessages++;
                        collectedMessages++;

                        var textNode = message.Node.SelectSingleNode(
                            ".//*[contains(" +
                            "concat(' ', normalize-space(@class), ' '), " +
                            "' tgme_widget_message_text ')]");

                        if (textNode is null)
                            continue;

                        foreach (var br in textNode
                            .Descendants("br").ToList())
                        {
                            br.ParentNode?.ReplaceChild(
                                document.CreateTextNode("\n"),
                                br);
                        }

                        var messageText = HtmlEntity.DeEntitize(
                            textNode.InnerText).Trim();

                        if (string.IsNullOrWhiteSpace(messageText))
                            continue;

                        foreach (var car in ExtractCars(messageText))
                        {
                            // Deduplicate identical extraction from
                            // the same Telegram message.
                            var key =
                                $"{message.Id}|{car.Name}|{car.Year}|{car.Price}";

                            if (seenCars.Add(key))
                                cars.Add(car);
                        }
                    }

                    if (newMessages == 0)
                        break;

                    var nextBefore = seenMessageIds.Min();

                    if (before.HasValue && nextBefore >= before.Value)
                        break;

                    before = nextBefore;

                    await Task.Delay(
                        800,
                        cancellationToken);
                }

                log?.Invoke(
                    $"{channel}: {collectedMessages} messages processed.");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke($"{channel}: {ex.Message}");
            }

            await Task.Delay(800, cancellationToken);
        }

        return cars;
    }

    private static long ReadMessageId(string value)
    {
        var lastSlash = value.LastIndexOf('/');

        return long.TryParse(
            value[(lastSlash + 1)..],
            out var id)
            ? id
            : 0;
    }

    private static IEnumerable<TelegramCar> ExtractCars(string message)
    {
        var text = Normalize(message);

        // Each advertised vehicle should have its own price.
        // Once a price is read, the accumulated block is processed.
        var lines = Regex.Split(text, @"[\r\n]+");
        var block = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Prevent contact details and installment amounts
            // from being interpreted as vehicle prices.
            if (Regex.IsMatch(
                line,
                @"(?:قسط|اقساط|پیش\s*پرداخت|بیعانه|اجاره)" ) &&
                !Regex.IsMatch(line, @"قیمت\s*نقدی"))
            {
                continue;
            }

            block.Add(line);

            var label = PriceLabelRegex.Match(line);
            var pricePart = label.Success
                ? line[(label.Index + label.Length)..]
                : line;

            var priceMatch = PriceRegex.Match(pricePart);

            // A label followed by prose/contact details is not an asking price.
            // Looking for a number later in the line used to turn phone numbers
            // into seemingly valid multi-billion-toman prices.
            if (!priceMatch.Success ||
                !string.IsNullOrWhiteSpace(pricePart[..priceMatch.Index]))
            {
                if (block.Count > 8)
                    block.RemoveAt(0);

                continue;
            }

            var hasExplicitUnit =
                priceMatch.Groups["unit"].Success ||
                priceMatch.Groups["currency"].Success;

            var startsWithPrice = Regex.IsMatch(
                line,
                @"^\s*\d[\d,٬.]*\s*" +
                @"(?:میلیارد|میلیون|م(?![\p{L}\p{N}])|تومان|ریال)");

            if (!label.Success && !startsWithPrice)
            {
                if (block.Count > 8)
                    block.RemoveAt(0);

                continue;
            }

            if (!hasExplicitUnit &&
                !label.Success)
            {
                continue;
            }

            var candidate = string.Join("\n", block);

            block.Clear();

            if (Regex.IsMatch(candidate, @"توافقی|قیمت\s*در\s*دایرکت"))
                continue;

            var yearMatches = LabeledYearRegex
                .Matches(candidate)
                .Select(x => x.Groups["year"].Value)
                .Distinct()
                .ToArray();

            // A year without a "model/year" label is supported,
            // but only when there is a single unambiguous year.
            if (yearMatches.Length == 0)
            {
                yearMatches = YearRegex.Matches(candidate)
                    .Select(x => x.Value)
                    .Distinct()
                    .ToArray();
            }

            if (yearMatches.Length != 1 ||
                !int.TryParse(yearMatches[0], out var year))
                continue;

            if (!TryParsePrice(priceMatch, out var price))
                continue;

            var currentLineStart = candidate.LastIndexOf(
                line,
                StringComparison.Ordinal);

            if (currentLineStart < 0)
                continue;

            var priceStart =
                currentLineStart +
                (label.Success ? label.Index + label.Length : 0) +
                priceMatch.Index;

            var nameText = candidate[..priceStart];

            nameText = LabeledYearRegex.Replace(nameText, " ");
            nameText = YearRegex.Replace(nameText, " ");

            var nameLines = nameText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanName)
                .Where(x => x.Length >= 2)
                .ToArray();

            var name = nameLines.LastOrDefault(x =>
                Regex.IsMatch(x, @"[\p{L}]") &&
                !Regex.IsMatch(x,
                    @"^(?:مدل|سال|قیمت|کارکرد|بیمه|تماس|تلفن|موبایل)\b"));

            if (string.IsNullOrWhiteSpace(name))
                continue;

            yield return new TelegramCar(
                name,
                year,
                price);
        }
    }

    private static bool TryParsePrice(
        Match match,
        out decimal price)
    {
        price = 0;

        if (!TryNumber(
            match.Groups["number"].Value,
            out var number))
            return false;

        var unit = match.Groups["unit"].Value;
        var currency = match.Groups["currency"].Value;

        var digits = Regex.Replace(match.Groups["number"].Value, @"\D", "");
        // Iranian mobile numbers, with or without their leading zero, cannot
        // be treated as bare prices even when a generic 'price' label exists.
        if (unit.Length == 0 && currency.Length == 0 &&
            (Regex.IsMatch(digits, @"^09\d{9}$") ||
             Regex.IsMatch(digits, @"^9\d{9}$")))
            return false;

        var multiplier = unit switch
        {
            "میلیارد" => 1_000_000_000m,
            "میلیون" or "م" => 1_000_000m,
            "هزار" => 1_000m,
            _ => 1m
        };

        // Bare amounts such as "2950" are ambiguous.
        // Do not guess their unit.
        if (multiplier == 1m &&
            number < 10_000_000m)
            return false;

        price = number * multiplier;

        if (match.Groups["extra"].Success)
        {
            if (!TryNumber(
                match.Groups["extra"].Value,
                out var extra))
                return false;

            var extraMultiplier =
                match.Groups["extraUnit"].Value == "میلیون"
                    ? 1_000_000m
                    : 1_000m;

            price += extra * extraMultiplier;
        }

        if (currency.Equals("ریال", StringComparison.OrdinalIgnoreCase) ||
            currency.Equals("IRR", StringComparison.OrdinalIgnoreCase))
        {
            price /= 10m;
        }

        return price is >= 10_000_000m
            and <= 100_000_000_000m;
    }

    private static bool TryNumber(
        string input,
        out decimal number)
    {
        input = input
            .Replace(",", "")
            .Replace("٬", "")
            .Replace("٫", ".");

        if (Regex.IsMatch(input, @"^\d{1,3}\.\d{3}$"))
            input = input.Replace(".", "");

        return decimal.TryParse(
            input,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static string CleanName(string input)
    {
        input = Regex.Replace(
            input,
            @"^(?:فروش(?:\s+فوری)?|نام\s*خودرو|خودرو)\s*[:：\-]?\s*",
            "",
            RegexOptions.IgnoreCase);

        input = Regex.Replace(
            input,
            @"\s+(?:کارکرد|کیلومتر|بیمه|شماره\s*تماس|تلفن)\s*[:：]?.*$",
            "",
            RegexOptions.IgnoreCase);

        input = Regex.Replace(
            input,
            @"[^\p{L}\p{N}\s+\-]",
            " ");

        return Regex.Replace(input, @"\s+", " ").Trim();
    }

    private static string Normalize(string input)
    {
        var builder = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            var digit = CharUnicodeInfo
                .GetDecimalDigitValue(character);

            builder.Append(
                digit >= 0
                    ? (char)('0' + digit)
                    : character);
        }

        return builder.ToString()
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace('\u200c', ' ');
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
