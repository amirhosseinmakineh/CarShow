using System.Net;
using CarShow.Infrastracture.Telegram;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAILED: {name}");
    Console.WriteLine($"PASS {name}"); passed++;
}
var single = TelegramCarService.ExtractCars("پژو ۲۰۷ اتومات مدل ۱۴۰۱\nقیمت: ۹۰۰ میلیون تومان");
Check(single.Count == 1 && single[0].Year == 1401 && single[0].Price == 900_000_000m && single[0].Name == "پژو 207 اتومات", "Persian digits and labeled year");
var sameLine = TelegramCarService.ExtractCars("پژو ۲۰۷ مدل ۱۴۰۱ قیمت ۹۰۰ میلیون");
Check(sameLine.Count == 1 && sameLine[0].Name == "پژو 207", "Same-line label removed from name");
var multiple = TelegramCarService.ExtractCars("تارا مدل ۱۴۰۲\nقیمت ۹۴۰ میلیون\nدنا مدل ۱۴۰۳\nقیمت ۱ میلیارد و ۲۰۰ میلیون");
Check(multiple.Count == 2 && multiple[1].Name == "دنا" && multiple[1].Price == 1_200_000_000m, "Two cars in one message");
Check(TelegramCarService.ExtractCars("دنا مدل ۱۴۰۲\nقیمت توافقی").Count == 0, "Negotiable price ignored");
Check(TelegramCarService.ExtractCars("دنا مدل ۱۴۰۱ مدل ۱۴۰۲\nقیمت ۹۰۰ میلیون").Count == 0, "Conflicting years ignored");
Check(TelegramCarService.ExtractCars("تارا مدل ۱۴۰۲\nقسط ۲۵ میلیون\nقیمت ۸۵۰ میلیون").Single().Price == 850_000_000m, "Installment ignored");
Check(TelegramCarService.ExtractCars("دنا مدل ۱۴۰۱\nقیمت ۹٬۰۰۰٬۰۰۰٬۰۰۰ ریال").Single().Price == 900_000_000m, "Rial conversion");
Check(TelegramCarService.ExtractCars("دنا مدل ۱۴۰۱\nقیمت ۲۹۵۰").Count == 0, "Bare ambiguous price ignored");
Check(TelegramCarService.Channels.Count == 84 && TelegramCarService.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 84, "Approved public usernames unique");
var unavailable = await new TelegramCarService(new HttpClient(new UnavailableHandler())).GetCarsAsync(1, "autopalermo");
Check(unavailable.Channels.Single().Error == "Telegram page unavailable from this network", "Upstream unavailable response detected");
var client = new HttpClient(new FakeHandler()) { Timeout = TimeSpan.FromSeconds(5) };
var result = await new TelegramCarService(client).GetCarsAsync(5, "autopalermo");
Check(result.Cars.Count == 1 && result.Cars[0].MessageId == 123 && result.Cars[0].Channel == "autopalermo", "HTML scan and repeated page dedupe");
Check(result.Channels.Single().MessagesProcessed == 1, "Message count");
try { await new TelegramCarService(client).GetCarsAsync(2, "outside-channel"); throw new Exception("FAILED: unknown channel allowed"); }
catch (ArgumentException) { Check(true, "Unknown channel rejected"); }
Console.WriteLine($"{passed} checks passed");
if (args.Contains("--live"))
{
    using var liveClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    var live = await new TelegramCarService(liveClient).GetCarsAsync(2, "autopalermo");
    Console.WriteLine($"LIVE channel={live.Channels.Single().Channel} messages={live.Channels.Single().MessagesProcessed} cars={live.Cars.Count} error={live.Channels.Single().Error ?? "none"}");
    if (live.Channels.Single().MessagesProcessed == 0) Environment.ExitCode = 1;
}

sealed class FakeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var html = "<div class='tgme_widget_message' data-post='autopalermo/123'><div class='tgme_widget_message_text'>دنا مدل ۱۴۰۱<br>قیمت ۹۰۰ میلیون</div></div>";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
    }
}

sealed class UnavailableHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<title>Site Unavailable</title>") });
}
