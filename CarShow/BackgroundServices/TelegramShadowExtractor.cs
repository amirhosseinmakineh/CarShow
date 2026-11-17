using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CarShow.BackgroundServices;

/// <summary>
/// Read-only exploratory token extraction. This is NOT the production price parser.
/// Candidates are deliberately not treated as validated vehicle asking prices.
/// </summary>
internal static class TelegramShadowExtractor
{
    private static readonly Regex YearToken = new(
        @"(?<![\p{L}\d])(?:14\d{2}|13\d{2}|20\d{2}|40[0-9]|0[0-9])(?!\d)",
        RegexOptions.Compiled);
    private static readonly Regex NumericToken = new(
        @"(?<![\p{L}\d])\d{1,3}(?:[,./٫]\d{3,6})+(?!\d)|(?<![\p{L}\d])\d{3,12}(?!\d)",
        RegexOptions.Compiled);
    private static readonly Regex PhoneToken = new(
        @"(?<!\d)(?:\+?98|0)9[\s\-\u200c]*\d(?:[\s\-\u200c]*\d){9}(?!\d)",
        RegexOptions.Compiled);

    internal sealed record Token(string Raw, string Normalized, string Kind, int Line, string Context);
    internal sealed record Observation(IReadOnlyList<Token> YearCandidates, IReadOnlyList<Token> NumericCandidates);

    internal static Observation Inspect(string rawMessage)
    {
        var years = new List<Token>();
        var numbers = new List<Token>();
        var lines = (rawMessage ?? "").Replace("\r", "").Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var original = lines[lineIndex];
            var normalized = Normalize(original);
            // Phone numbers are not price candidates; preserve offsets by masking them.
            var masked = PhoneToken.Replace(normalized, m => new string(' ', m.Length));
            var mileage = Regex.IsMatch(normalized, @"کارکرد|کیلومتر|آمپر|\bkm\b|هزار\s*تا");
            var accessory = Regex.IsMatch(normalized, @"کاور|لوازم|رینگ|بیمه|مالیات");
            var payment = Regex.IsMatch(normalized, @"پیش\s*پرداخت|بیعانه|اقساط|قسط|ودیعه");
            foreach (Match match in YearToken.Matches(masked))
            {
                var value = match.Value;
                // 405 is a car model in 'پژو 405', not a manufacturing year.
                if (value == "405" && Regex.IsMatch(masked[..match.Index], @"پژو\s*$"))
                    continue;
                years.Add(new Token(original.Substring(match.Index, match.Length),
                    value, "YearCandidate", lineIndex, original.Trim()));
            }
            foreach (Match match in NumericToken.Matches(masked))
            {
                var value = match.Value;
                // Years are reported separately. 405/06 must not be a price.
                if (YearToken.IsMatch(value) && YearToken.Match(value).Length == value.Length)
                    continue;
                if (Regex.IsMatch(value, @"^(?:14\d{2}|40[0-9])[/.-](?:0?[1-9]|1[0-2])$"))
                    continue;
                var kind = mileage ? "MileageOrUncertain" :
                    accessory ? "AccessoryOrUncertain" :
                    payment ? "PaymentOrUncertain" : "UnclassifiedNumber";
                numbers.Add(new Token(original.Substring(match.Index, match.Length),
                    value, kind, lineIndex, original.Trim()));
            }
        }
        return new Observation(years, numbers);
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c >= '۰' && c <= '۹') sb.Append((char)('0' + c - '۰'));
            else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + c - '٠'));
            else sb.Append(c switch { '٫' => '.', 'ي' => 'ی', 'ك' => 'ک', _ => c });
        }
        return sb.ToString().ToLowerInvariant();
    }
}
