using System.Reflection;

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
var channels = (string[])typeof(TelegramCarService).GetField("Channels", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
Check(channels.Length == 84 && channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 84, "Approved public usernames unique");
Console.WriteLine($"{passed} checks passed");
