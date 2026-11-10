using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CarShow.BackgroundServices;

/// <summary>
/// Extracts only explicit trim evidence from an advertisement. Never guesses a trim.
/// A null result means unknown or conflicting trim and must not be priced.
/// </summary>
public static class TelegramCarTrimExtractor
{
    public static string? Extract(string? vehicleName, string? advertisementText = null)
    {
        if (string.IsNullOrWhiteSpace(vehicleName)) return null;
        // Vehicle header is the trusted source: body text may describe other vehicles.
        var text = Normalize(vehicleName);
        var codes = new List<string>();

        // Version codes must be separate tokens, not part of years, phone numbers or prices.
        AddMatches(text, @"(?<![a-z0-9])v[1-9](?:\d)?(?![a-z0-9])", codes);
        AddMatches(text, @"(?<![a-z0-9])(?:tu3|tu5|tu5p|ef7|ef7p|glx|slx|lx|ex|cvt)(?![a-z0-9])", codes);
        AddMatches(text, @"(?<![a-z0-9])(?:x22|x33|x55|x77|s5|s7|j4|j7)(?![a-z0-9])", codes);

        // Numeric model versions require an explicit model family, so price digits
        // cannot accidentally be interpreted as trim numbers.
        var numbered = Regex.Match(text, @"\b(?:اریزو|آریزو|تیگو|فیدلیتی|هایما|ام وی ام)\s*(?<n>[357689])\b");
        if (numbered.Success) codes.Add(numbered.Groups["n"].Value);

        var automatic = Has(text, @"(?:اتوماتیک|اتومات|اتو|automatic|auto)");
        var manual = Has(text, @"(?:دستی|دنده ای|manual)");
        if (automatic && manual) return null;
        if (automatic) codes.Add("اتومات");
        if (manual) codes.Add("دستی");

        foreach (var (pattern, value) in new (string, string)[]
        {
            (@"توربو", "توربو"), (@"پلاس|plus", "پلاس"),
            (@"پرو|pro", "پرو"), (@"پرایم|prime", "پرایم"),
            (@"الیت|elite", "الیت"), (@"اکسلنت|excellent", "اکسلنت"),
            (@"فول", "فول"), (@"نیمه فول", "نیمه فول")
        })
            if (Has(text, pattern)) codes.Add(value);

        // "Full" is not a reliable trim on its own.
        if (codes.Count == 0 || (codes.Count == 1 && codes[0] == "فول"))
            return null;

        return string.Join(" ", codes.Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal));
    }

    private static bool Has(string text, string pattern) =>
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])(?:" + pattern + @")(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void AddMatches(string text, string pattern, List<string> codes)
    {
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
            codes.Add(match.Value);
    }

    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + c - '۰'),
                >= '٠' and <= '٩' => (char)('0' + c - '٠'),
                'ي' => 'ی',
                'ك' => 'ک',
                'آ' or 'أ' or 'إ' => 'ا',
                '\u200c' => ' ',
                _ => char.ToLowerInvariant(c)
            });
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
