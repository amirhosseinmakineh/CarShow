using System.Reflection;
using System.Net;

var method = typeof(TelegramCarService).GetMethod("ExtractCars", BindingFlags.NonPublic | BindingFlags.Static)!;
List<TelegramCar> Parse(string text) => ((IEnumerable<TelegramCar>)method.Invoke(null, [text])!).ToList();
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAILED: {name}");
    Console.WriteLine($"PASS {name}"); passed++;
}
var single = Parse("پژو ۲۰۷ اتومات مدل ۱۴۰۱\nقیمت: ۹۰۰ میلیون تومان");
Check(single.Count == 1 && single[0].Year == 1401 && single[0].Price == 900_000_000m && single[0].Name == "پژو 207 اتومات", "Persian digits and labeled year");
var multiple = Parse("تارا مدل ۱۴۰۲\nقیمت ۹۴۰ میلیون\nدنا مدل ۱۴۰۳\nقیمت ۱ میلیارد و ۲۰۰ میلیون");
Check(multiple.Count == 2 && multiple[1].Name == "دنا" && multiple[1].Price == 1_200_000_000m, "Two cars in one message");
Check(Parse("دنا مدل ۱۴۰۲\nقیمت توافقی").Count == 0, "Negotiable price ignored");
Check(Parse("دنا مدل ۱۴۰۱ مدل ۱۴۰۲\nقیمت ۹۰۰ میلیون").Count == 0, "Conflicting years ignored");
Check(Parse("تارا مدل ۱۴۰۲\nقسط ۲۵ میلیون\nقیمت ۸۵۰ میلیون").Single().Price == 850_000_000m, "Installment ignored");
Check(Parse("دنا مدل ۱۴۰۱\nقیمت ۹٬۰۰۰٬۰۰۰٬۰۰۰ ریال").Single().Price == 900_000_000m, "Rial conversion");
Check(Parse("دنا مدل ۱۴۰۱\nقیمت ۲۹۵۰").Count == 0, "Bare ambiguous price ignored");
Check(Parse("دنا اتومات سفید مدل ۱۴۰۲\nقیمت تماس ۰۹۱۹۷۱۵۶۰۰۷").Count == 0,
    "Contact number after price label is not a price");
Check(Parse("کرولا الیت نیو فیس مدل ۲۰۲۶\nقیمت ۹۱۲۳۴۴۹۶۸۴").Count == 0,
    "Mobile number without zero is not a bare price");
Check(Parse("دنا مدل ۱۴۰۲\nقیمت ۹۰۰۰۰۰۰۰۰ تومان").Single().Price == 900_000_000m,
    "Explicit full toman price remains valid");
var channels = (string[])typeof(TelegramCarService).GetField("Channels", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
Check(channels.Length == 84 && channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 84, "Approved public usernames unique");
var requested = new List<Uri>();
var samples = new List<TelegramMessageSample>();
using (var client = new HttpClient(new StubTelegramHandler(requested)))
using (var service = new TelegramCarService(client))
{
    var scanned = await service.GetCarsAsync(1, selectedChannel: "AUTOPALERMO",
        onMessage: samples.Add);
    Check(requested.Count == 1 && requested[0].AbsolutePath == "/s/autopalermo" &&
          scanned.Single().Price == 900_000_000m, "Single approved channel scan");
    Check(samples.Count == 1 && samples[0].Channel == "autopalermo" &&
          samples[0].MessageId == 42 && samples[0].Text.Contains("قیمت ۹۰۰ میلیون"),
        "Raw Telegram text available to admin preview");
    try
    {
        await service.GetCarsAsync(1, selectedChannel: "not_approved");
        throw new Exception("Unknown channel was accepted");
    }
    catch (ArgumentException) { Check(true, "Unknown channel rejected"); }
}
Console.WriteLine($"{passed} checks passed");

sealed class StubTelegramHandler(List<Uri> requested) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requested.Add(request.RequestUri!);
        const string html = "<div class='tgme_widget_message' data-post='autopalermo/42'>" +
            "<div class='tgme_widget_message_text'>پژو ۲۰۷ مدل ۱۴۰۱<br>قیمت ۹۰۰ میلیون</div></div>";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        });
    }
}
