using CarShow.BackgroundServices;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var samples = new (string Message, string Year, string Number)[]
{
    ("تارا دنده V1 مدل ۱۴۰۵ روز🔷۳/۳۰۰", "۱۴۰۵", "۳/۳۰۰"),
    ("کرولا کراس ۲۰۲۶ پلاک تهران 10/550", "۲۰۲۶", "10/550"),
    ("فیدلیتی الیت ۴۰۵/۰۳\n7/850", "۴۰۵", "7/850"),
    ("شاهین پلاس اتومات\n4,008,000,000", "", "4,008,000,000"),
    ("۲۰۷ دنده پانا ۱۴۰۵ برج ۱۲\n3/415", "۱۴۰۵", "3/415"),
};
foreach (var sample in samples)
{
    var tokens = TelegramShadowExtractor.Inspect(sample.Message);
    if (sample.Year != "")
        Assert(tokens.YearCandidates.Any(t => t.Raw == sample.Year), "Missing year: " + sample.Message);
    Assert(tokens.NumericCandidates.Any(t => t.Raw == sample.Number), "Missing numeric token: " + sample.Message);
}
var accessory = TelegramShadowExtractor.Inspect("دیگنیتی ۴۰۳ کارکرد ۱۹ هزار کاور بدنه ۳۰۰ میلیونی");
Assert(accessory.NumericCandidates.All(t => t.Kind != "VehiclePrice"), "Accessory must not be accepted as sale price");
var phone = TelegramShadowExtractor.Inspect("۲۰۷ ۴۰۵ روز\n09123456789");
Assert(phone.NumericCandidates.All(t => !t.Raw.Contains("09123456789")), "Phone incorrectly extracted");
var missing = TelegramShadowExtractor.Inspect("تیگو ۸ پرومکس موجود");
Assert(missing.NumericCandidates.Count == 0, "Invented price");
Console.WriteLine("Shadow extractor smoke tests passed.");
