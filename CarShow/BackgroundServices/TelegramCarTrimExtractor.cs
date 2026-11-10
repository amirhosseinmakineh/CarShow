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
        ("TU5P", @"tu\s*5\s*p"), ("TU5", @"tu\s*5"), ("TU3", @"tu\s*3"),
        ("EF7P", @"ef\s*7\s*p"), ("EF7", @"ef\s*7"),
        ("GLX", @"glx"), ("SLX", @"slx"), ("LX", @"lx"),
        ("EX", @"ex"), ("CVT", @"cvt"),
        ("اتومات", @"اتوماتیک|اتومات|اتو|automatic|auto"),
        ("دستی", @"دستی|دنده\s*ای|manual"),
        ("توربو", @"توربو|turbo"),
        ("پلاس", @"پلاس|plus"), ("پرو", @"پرو|pro"),
        ("پرایم", @"پرایم|prime"), ("الیت", @"الیت|elite"),
        ("اکسلنت", @"اکسلنت|excellent")
    };

    // Model families whose numeric version is a meaningful identity component.
    private static readonly string[] NumericFamilies =
    {
        "اریزو", "تیگو", "هایما", "ام وی ام", "فیدلیتی"
    };

    public static string? Extract(string? vehicleName, string? advertisementText = null)
    {
        if (string.IsNullOrWhiteSpace(vehicleName)) return null;
        var header = Normalize(vehicleName);
        var body = Normalize(advertisementText ?? string.Empty);
        var model = IdentifyModel(header);
        var headerTokens = Recognize(header, model);
        var bodyTokens = Recognize(RemoveNoise(body), model);

        // Reject contradictory explicit gearbox and version claims, including
        // disagreements between title and the corresponding vehicle's body.
        var all = headerTokens.Concat(bodyTokens).Distinct(StringComparer.Ordinal).ToList();
        if (all.Contains("اتومات") && all.Contains("دستی")) return null;
        if (all.Count(x => Regex.IsMatch(x, @"^V[1-4]$")) > 1) return null;
        if (all.Count(x => x is "TU3" or "TU5" or "TU5P") > 1) return null;
        if (all.Count(x => x is "EF7" or "EF7P") > 1) return null;
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
            if (canonical.StartsWith("V", StringComparison.Ordinal) && model != "تارا")
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
