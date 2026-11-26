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
var parser = scanner.Assembly.GetType("CarShow.BackgroundServices.ParserService")
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

var normalize = parser.GetMethod("NormalizeArchiveYears", BindingFlags.NonPublic | BindingFlags.Static)!;
string NormalizeArchive(string message, params string[] models) =>
    (string)normalize.Invoke(null, new object[] { message, models })!;
Check(NormalizeArchive("لوکانو l7 سفید 405 برج روز", "لوکانو l7") == "لوکانو l7 سفید 2026 برج روز", "Short archive year");
Check(NormalizeArchive("دنا سفید 1405/3", "دنا") == "دنا سفید 2026/3", "Full solar year with month");
Check(NormalizeArchive("شاهین سفید 1405برج5", "شاهین") == "شاهین سفید 2026برج5", "Attached year");
Check(NormalizeArchive("پژو 405", "پژو 405") == "پژو 405", "Numeric model preserved");
Check(NormalizeArchive("آریزو 8 مشکی 403 تحویل 404", "آریزو 8") == "آریزو 8 مشکی 2024 تحویل 404", "Delivery year preserved");
Check(Parse("دنا سفید مدل 1405\n3/385/000", "دنا")[0].Price == "3385", "Grouped price");
var extractArchive = parser.GetMethod("ExtractArchive", BindingFlags.NonPublic | BindingFlags.Static)!;
System.Text.Json.JsonElement ExtractArchive(string message)
{
    var payload = extractArchive.Invoke(null, new object[] { message });
    return System.Text.Json.JsonSerializer.SerializeToElement(payload);
}
Check(ExtractArchive("تسلیت و تشکر").GetProperty("Vehicles").GetArrayLength() == 0, "Unrelated archive message ignored");
var extractedVehicle = ExtractArchive("دنا پلاس توربو سفید 405 برج 7\n4380").GetProperty("Vehicles")[0];
Check(extractedVehicle.GetProperty("CarId").ValueKind == System.Text.Json.JsonValueKind.Null && extractedVehicle.GetProperty("Year").GetString() == "2026", "Archive independent of catalog");
Check(ExtractArchive("خریدار دنا پلاس توربو سفید 405 برج 7\n4380").GetProperty("Vehicles").GetArrayLength() == 0, "Buyer ignored");
Check(ExtractArchive("دنا پلاس سفید").GetProperty("Vehicles").GetArrayLength() == 0, "Incomplete vehicle ignored");
Check(ExtractArchive("شاهین سفید 1405\n1850").GetProperty("Vehicles").GetArrayLength() == 1, "Trim optional");
var compareArchive = scanner.GetMethod("CompareArchive", BindingFlags.NonPublic | BindingFlags.Static)!;
var archiveType = compareArchive.GetParameters()[0].ParameterType;
var archive = (System.Collections.IDictionary)Activator.CreateInstance(archiveType)!;
var messages = (System.Collections.IList)Activator.CreateInstance(archiveType.GetGenericArguments()[1])!;
foreach (var message in new[]
{
    "دنا اپشنال اتومات 1405\nقیمت: 4,355,000,000 تومان",
    "دنا اپشنال اتوماتیک 1405\nقیمت: 4,360,000,000 تومان",
    "دنا اپشنال دنده 1405\nقیمت: 4,350,000,000 تومان",
    "دنا اپشنال اتوماتیک 1404\nقیمت: 4,345,000,000 تومان"
})
{
    messages.Add(extractArchive.Invoke(null, new object[] { message }));
}
archive.Add("archive-regression", messages);
var comparison = System.Text.Json.JsonSerializer.SerializeToElement(
    compareArchive.Invoke(null, new object[] { archive, CancellationToken.None }));
var archiveGroups = comparison.GetProperty("Groups").EnumerateArray().ToArray();
Check(archiveGroups.Length == 3, "Archive comparison keeps different trims and years separate");
var approvedGroup = archiveGroups.Single(group => group.GetProperty("Reference").ValueKind != System.Text.Json.JsonValueKind.Null);
Check(approvedGroup.GetProperty("Trim").GetString() == "اپشنال اتوماتیک"
    && approvedGroup.GetProperty("Year").GetString() == "2026"
    && approvedGroup.GetProperty("CandidatePricesBillion").EnumerateArray()
        .Select(price => price.GetDecimal()).SequenceEqual(new[] { 4.355m, 4.360m }),
    "Archive comparison retains both advertisements after normalizing اتومات to اتوماتیک");

var parsed = Parse("تیگو 8 پرو مکس مدل 1404\nقیمت 4 تومن", "تیگو");
Check(parsed.Count == 1 && parsed[0].Year == "2025" && parsed[0].Price == "4 میلیارد",
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
    is [{ Year: "2025", AdvertisementCount: 1 }, { Year: "2026", AdvertisementCount: 1 }],
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
Check(TelegramScanService.MatchCatalogCar("پژو 207 اتوماتیک", catalog) == null,
    "Duplicate normalized automatic trims are ambiguous");
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
await CatalogUploadChecks.RunAsync(Check);
