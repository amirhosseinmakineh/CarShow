using System.Reflection;
using CarShow.BackgroundServices;

var count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    count++;
}

var scanner = typeof(TelegramScanService);
var parser = scanner.GetNestedType("TelegramCarParser", BindingFlags.NonPublic)
    ?? throw new Exception("Telegram parser missing");
var parseMethod = parser.GetMethod("ParseCatalog", BindingFlags.Public | BindingFlags.Static)
    ?? throw new Exception("ParseCatalog missing");
var builder = scanner.GetNestedType("CarPriceReferenceBuilder", BindingFlags.NonPublic)
    ?? throw new Exception("Reference builder missing");
var buildMethod = builder.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)
    ?? throw new Exception("Build missing");

List<TelegramScanService.ParsedCar> Parse(string message, params string[] models) =>
    (List<TelegramScanService.ParsedCar>)parseMethod.Invoke(null, new object[] { message, models })!;

List<TelegramScanService.CarPriceReference> Build(params TelegramScanService.ParsedCar[] cars) =>
    (List<TelegramScanService.CarPriceReference>)buildMethod.Invoke(null,
        new object[] { cars.AsEnumerable() })!;

TelegramScanService.ParsedCar Ad(string year, string price) =>
    new() { Name = "catalog:75354", Year = year, Price = price };

var parsed = Parse("تیگو 8 پرو مکس مدل 1404\nقیمت 4 تومن", "تیگو");
Check(parsed.Count == 1 && parsed[0].Year == "1404" && parsed[0].Price == "4 میلیارد",
    "Colloquial 4 toman means 4 billion in car advertisements");

parsed = Parse("تیگو 8 پرو مکس مدل 1404\nقیمت 4200", "تیگو");
Check(parsed.Count == 1 && parsed[0].Price == "4200", "Labeled unitless prices remain supported");

parsed = Parse("تیگو 8 پرو مکس مدل 1404\nکد 8000", "تیگو");
Check(parsed.Count == 1 && parsed[0].Price == null, "Unlabeled numbers are not assumed prices");

parsed = Parse("تیگو 8 پرو مکس مدل 1404\nپیش پرداخت 4 تومن", "تیگو");
Check(parsed.Count == 1 && parsed[0].Price == null, "Down payments are not sale prices");

parsed = Parse("تیگو 8 پرو مکس مدل 1404\nقیمت 4 میلیارد", "تیگو");
Check(parsed.Count == 1 && parsed[0].Price == "4 میلیارد", "Explicit units are not double-counted");

Check(Build(Ad("1405", "4290 میلیون"), Ad("1405", "4295 میلیون"), Ad("1405", "16 میلیارد"))
    is [{ ReferencePriceBillion: 4.293m, AdvertisementCount: 2 }],
    "Isolated extreme high outlier excluded");

Check(Build(Ad("1405", "7950 میلیون"), Ad("1405", "7960 میلیون"),
    Ad("1405", "912 میلیون")) is [{ AdvertisementCount: 2 }],
    "Isolated extreme low outlier excluded");

Check(Build(Ad("1405", "912 میلیون"), Ad("1405", "11840 میلیون")).Count == 0,
    "Two conflicting ads produce no reference");

Check(Build(Ad("1405", "4290 میلیون"), Ad("1404", "4295 میلیون"))
    is [{ Year: "1404", AdvertisementCount: 1 }, { Year: "1405", AdvertisementCount: 1 }],
    "Different model years produce separate single-ad references");

Check(Build(Ad("1405", "4290 میلیون"))
    is [{ ReferencePriceBillion: 4.290m, AdvertisementCount: 1 }],
    "Single validated advertisement produces its own reference");

Check(Build(Ad("1405", "نامشخص")).Count == 0,
    "Single advertisement with invalid price produces no reference");

Check(Build(Ad("نامشخص", "4290 میلیون")).Count == 0,
    "Single advertisement with invalid year produces no reference");

var catalog = new (long Id, string Model, string Tip)[]
{
    (1, "پژو 207", "اتومات"),
    (2, "پژو 207", "اتوماتیک"),
    (3, "پژو 207", "دنده ای"),
    (4, "پژو 207 sd", "اتوماتیک"),
    (5, "دنا", "اتوماتیک")
};
Check(TelegramScanService.MatchCatalogCar("پژو 207 اتوماتیک", catalog) == 2,
    "Trim selection respects token boundaries");
Check(TelegramScanService.MatchCatalogCar("پژو 207 sd اتوماتیک", catalog) == 4,
    "Specific model wins over contained shorter model");
Check(TelegramScanService.MatchCatalogCar("پژو 207 دنده ای", catalog) == 3,
    "Different trims stay distinct");
Check(TelegramScanService.MatchCatalogCar("پژو 207", catalog) == null,
    "Missing trim does not guess");
Check(TelegramScanService.MatchCatalogCar("پژو 207 اتوماتیک دنده ای", catalog) == null,
    "Conflicting trim evidence is rejected");
Check(Parse("پژو 207 اتوماتیک مدل 1404\nقیمت 4 میلیارد\nمعاوضه با پژو 207 دنده ای", "پژو 207").Count == 1,
    "Trade-in mention does not create another vehicle");

Console.WriteLine($"PASS: {count} scanner regression checks");
