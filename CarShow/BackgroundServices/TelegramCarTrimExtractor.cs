using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CarShow.BackgroundServices;

/// <summary>
/// Conservative, model-aware trim recognition. Null means unknown or contradictory;
/// no trim is inferred from price, model year or generic sales descriptions.
/// </summary>
public static class TelegramCarTrimExtractor
{
    private static readonly (string Canonical, string Pattern)[] Variants =
    {
        ("V1", @"v\s*1|وی\s*1"), ("V2", @"v\s*2|وی\s*2"),
        ("V3", @"v\s*3|وی\s*3"), ("V4", @"v\s*4|وی\s*4"),
        ("TU5P", @"tu\s*5\s*p"), ("TU5", @"tu\s*5(?!\s*p)"), ("TU3", @"tu\s*3"),
        ("EF7P", @"ef\s*7\s*p"), ("EF7", @"ef\s*7(?!\s*p)"),
        ("GLX", @"glx"), ("SLX", @"slx"), ("LX", @"lx"),
        ("EX", @"ex"), ("CVT", @"cvt"),
        ("اتومات", @"اتوماتیک|اتومات|اتو|automatic|auto"),
        ("دستی", @"دستی|دنده\s*ای|manual"),
        ("توربو", @"توربو|turbo"),
        ("پلاس", @"پلاس|plus"), ("پرو", @"پرو|pro"),
        ("پرایم", @"پرایم|prime"), ("الیت", @"الیت|elite"),
        ("اکسلنت", @"اکسلنت|excellent"),
        ("پرستیژ", @"پرستیژ|prestige"), ("پرمیوم", @"پرمیوم|premium"),
        ("لاکچری", @"لاکچری|luxury"), ("اسپرت", @"اسپرت|sport"),
        ("GT", @"gt"), ("GDI", @"gdi"), ("TGDI", @"tgdi"),
        ("AWD", @"awd|چهار\s*چرخ\s*متحرک"), ("4WD", @"4wd|دو\s*دیفرانسیل"),
        ("هیبرید", @"هیبرید|hybrid|hev"), ("PHEV", @"phev|پلاگین\s*هیبرید"),
        ("EV", @"ev|برقی"), ("فیس لیفت", @"فیس\s*لیفت|facelift"),
        ("نیوفیس", @"نیو\s*فیس|new\s*face"), ("تیپ 2", @"تیپ\s*2"),
        ("تیپ 3", @"تیپ\s*3"), ("تیپ 5", @"تیپ\s*5"),
        ("تیپ 6", @"تیپ\s*6")
    };


    // Explicit model/variant pairs cross-checked against car.ir/prices.
    // Model identifiers (X55, S7, 207) are not trims by themselves.
    private static readonly (string ModelPattern, string Trim, string TrimPattern)[] VerifiedModelTrims =
    {
        (@"(?:پژو\\s*)?206", "تیپ 2", @"تیپ\\s*2"),
        (@"(?:پژو\\s*)?206", "تیپ 3", @"تیپ\\s*3"),
        (@"(?:پژو\\s*)?207", "TU3", @"tu\\s*3"),
        (@"(?:پژو\\s*)?207", "اتومات", @"اتوماتیک|اتومات|automatic"),
        (@"(?:پژو\\s*)?207", "دستی", @"دستی|دنده\\s*ای"),
        (@"(?:پژو\\s*)?207", "سقف شیشه ای", @"سقف\\s*شیشه\\s*ای|پانوراما"),
        (@"تارا", "V1", @"v\\s*1|وی\\s*1"),
        (@"تارا", "V4", @"v\\s*4|وی\\s*4"),
        (@"دیگنیتی", "پرایم", @"پرایم|prime"),
        (@"دیگنیتی", "پرستیژ", @"پرستیژ|prestige"),
        (@"(?:اریزو|آریزو)\\s*5", "اسپرت", @"اسپرت|sport|fl"),
        (@"(?:اریزو|آریزو)\\s*6", "پرو", @"پرو|pro"),
        (@"تیگو\\s*7", "پرو", @"پرو|pro"),
        (@"تیگو\\s*7", "پرو مکس", @"پرو\\s*مکس|pro\\s*max"),
        (@"تیگو\\s*8", "پرو مکس", @"پرو\\s*مکس|pro\\s*max"),
        (@"ام\\s*وی\\s*ام\\s*x22", "پرو", @"پرو|pro"),
        (@"ام\\s*وی\\s*ام\\s*x33", "کراس", @"کراس|cross"),
        (@"ام\\s*وی\\s*ام\\s*x55", "پرو", @"پرو|pro"),
        (@"هایما\\s*s7", "پلاس", @"پلاس|plus"),
        (@"فونیکس\\s*fx", "پرمیوم", @"پرمیوم|premium"),
        (@"کوییک", "R", @"(?<![a-z0-9])r(?![a-z0-9])"),
        (@"کوییک", "S", @"(?<![a-z0-9])s(?![a-z0-9])"),
        (@"ساینا", "S", @"(?<![a-z0-9])s(?![a-z0-9])")
    };

    // Model families whose numeric version is a meaningful identity component.
    private static readonly string[] NumericFamilies =
    {
        "اریزو", "تیگو", "هایما", "ام وی ام"
    };

    public static string? Extract(string? vehicleName, string? advertisementText = null)
    {
        if (string.IsNullOrWhiteSpace(vehicleName)) return null;
        var header = Normalize(vehicleName);
        var body = Normalize(advertisementText ?? string.Empty);
        var model = IdentifyModel(header);
        var headerTokens = Recognize(header, model);
        var bodyTokens = Recognize(RemoveNoise(body), model);
        // If two different numeric versions are explicitly named, reject the ad.
        if (NumericFamilies.Contains(model, StringComparer.Ordinal))
        {
            var versions = Regex.Matches(header + " " + RemoveNoise(body),
                @"(?<![\p{L}\p{N}])" + Regex.Escape(model) +
                @"\s*([357689])(?![\p{L}\p{N}])")
                .Cast<Match>().Select(match => match.Groups[1].Value).Distinct().ToList();
            if (versions.Count > 1) return null;
        }

        // Reject contradictory explicit gearbox and version claims, including
        // disagreements between title and the corresponding vehicle's body.
        var all = headerTokens.Concat(bodyTokens).Distinct(StringComparer.Ordinal).ToList();
        foreach (var rule in VerifiedModelTrims)
        {
            if (Has(header, rule.ModelPattern) && Has(header + " " + RemoveNoise(body), rule.TrimPattern))
                all.Add(rule.Trim);
        }
        if (all.Contains("پرو مکس")) all.Remove("پرو");
        all = all.Distinct(StringComparer.Ordinal).ToList();
        if (all.Contains("اتومات") && all.Contains("دستی")) return null;
        if (all.Contains("پرایم") && all.Contains("پرستیژ")) return null;
        if (all.Contains("R") && all.Contains("S")) return null;
        if (all.Count(x => Regex.IsMatch(x, @"^V[1-4]$")) > 1) return null;
        if (all.Count(x => x.StartsWith("نسخه ", StringComparison.Ordinal)) > 1) return null;
        if (all.Count(x => x is "TU3" or "TU5" or "TU5P") > 1) return null;
        if (all.Count(x => x is "EF7" or "EF7P") > 1) return null;
        if (all.Contains("GDI") && all.Contains("TGDI")) all.Remove("GDI");
        if (all.Contains("هیبرید") && all.Contains("PHEV")) all.Remove("هیبرید");
        if (all.Contains("EV") && (all.Contains("هیبرید") || all.Contains("PHEV"))) return null;
        if (all.Count(x => x is "GLX" or "SLX" or "LX" or "EX") > 1) return null;

        // A generic description such as "full", "zero", or "ready to deliver"
        // is never a trim. A model's numeric version is kept distinct.
        if (all.Count == 0) return null;
        return string.Join(" ", all.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static List<string> Recognize(string text, string model)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        // Only vehicle-specific version codes are recognized in the appropriate family.
        foreach (var (canonical, pattern) in Variants)
        {
            if (Regex.IsMatch(canonical, @"^V[1-4]$") && model != "تارا")
                continue;
            if (Has(text, pattern)) tokens.Add(canonical);
        }

        if (NumericFamilies.Contains(model, StringComparer.Ordinal))
        {
            var match = Regex.Match(text,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(model) +
                @"\s*(?<version>[357689])(?![\p{L}\p{N}])");
            if (match.Success) tokens.Add("نسخه " + match.Groups["version"].Value);
        }

        // Avoid interpreting a model number (X55, J7 etc.) as a generic trim:
        // the full model name remains part of the car identity.
        return tokens;
    }

    private static string IdentifyModel(string header)
    {
        foreach (var family in NumericFamilies.Concat(new[] { "تارا" }))
            if (Has(header, Regex.Escape(family))) return family;
        return string.Empty;
    }

    private static string RemoveNoise(string text)
    {
        // Exclude price, phone, mileage and year lines, which may contain
        // misleading tokens. Keep actual descriptive lines intact.
        return string.Join(" ", text.Split('\n')
            .Where(line => !Regex.IsMatch(line,
                @"(?:قیمت|مبلغ|تومان|میلیارد|میلیون|شماره|تماس|09\d{9}|کارکرد|کیلومتر|^\s*مدل\s*(?:13|14|20)\d{2}\s*$)")));
    }

    private static bool Has(string text, string pattern) =>
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])(?:" + pattern +
            @")(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        return Regex.Replace(sb.ToString(), @"[^\S\n]+", " ").Trim();
    }
}
