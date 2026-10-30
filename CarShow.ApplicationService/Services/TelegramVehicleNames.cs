using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace CarShow.ApplicationService.Services;

/// <summary>
/// Canonical model names and their public spelling variants. Each entry is one
/// vehicle identity; a message naming two identities requires human review.
/// Extend this catalog without changing the matching/pricing pipeline.
/// </summary>
internal sealed class TelegramVehicleNames
{
    private sealed record Entry(string Name, params string[] Aliases);

    private static readonly Entry[] Entries =
    [
        new("دنا پلاس", "دنا پلاس", "دنا+"),
        new("پژو پارس", "پژو پارس", "پارس سال", "پارس موتور xu7"),
        new("پژو 207", "پژو 207", "۲۰۷", "207"),
        new("پژو 206", "پژو 206", "206"),
        new("پژو 405", "پژو 405", "405"),
        new("KMC J7", "kmc j7", "کی ام سی j7"),
        new("شاهین", "شاهین"),
        new("تارا", "تارا"),
        new("کوییک", "کوییک"),
        new("ساینا", "ساینا"),
        new("سمند", "سمند"),
        new("رانا پلاس", "رانا پلاس"),
        new("رانا", "رانا"),
        new("دنا", "دنا"),
        new("تیبا", "تیبا"),
        new("پراید", "پراید"),
        new("هایما S7", "هایما s7"),
        new("هایما S5", "هایما s5"),
        new("جک S5", "جک s5"),
        new("جک J4", "جک j4"),
        new("فیدلیتی", "فیدلیتی"),
        new("دیگنیتی", "دیگنیتی"),
        new("لاماری", "لاماری"),
        new("ریسپکت", "ریسپکت"),
        new("کمری", "کمری"),
        new("کرولا", "کرولا"),
        new("توسان", "توسان"),
        new("سانتافه", "سانتافه"),
        new("اسپورتیج", "اسپورتیج"),
        new("سراتو", "سراتو"),
        new("النترا", "النترا")
    ];
    private readonly (string Name, Regex[] Patterns)[] patterns;

    public TelegramVehicleNames(IConfiguration? configuration)
    {
        // Operators can extend the catalog via Telegram:VehicleAliases:<canonical>:<index>
        // without editing code or redeploying the extraction rules.
        var configured = configuration?.GetSection("Telegram:VehicleAliases").GetChildren()
            .Select(section => new Entry(section.Key,
                section.GetChildren().Select(alias => alias.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!).Prepend(section.Key).ToArray())) ?? [];
        patterns = Entries.Concat(configured).Select(entry =>
        (entry.Name, entry.Aliases.Select(alias => new Regex(
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(CarListingMatcher.Normalize(alias).Replace("+", " پلاس", StringComparison.Ordinal))}(?![\p{{L}}\p{{N}}])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase)).ToArray())).ToArray();
    }

    public string? FindUnique(string text, out bool ambiguous, out string? rawName)
    {
        var normalized = CarListingMatcher.Normalize(text).Replace("+", " پلاس", StringComparison.Ordinal);
        var matches = patterns.Select(entry => new
            {
                entry.Name,
                Match = entry.Patterns.Select(pattern => pattern.Match(normalized))
                    .Where(match => match.Success).OrderByDescending(match => match.Length).FirstOrDefault()
            }).Where(entry => entry.Match is not null)
            .GroupBy(entry => entry.Name).Select(group => group.OrderByDescending(entry => entry.Match!.Length).First())
            .ToArray();
        // An abbreviated base model inside its more specific variant is not a second car.
        matches = matches.Where(entry => !matches.Any(other => other.Name != entry.Name
                && other.Name.StartsWith(entry.Name + " ", StringComparison.Ordinal)))
            .ToArray();
        ambiguous = matches.Length > 1;
        rawName = matches.Length == 1 ? matches[0].Match!.Value : null;
        return matches.Length == 1 ? matches[0].Name : null;
    }
}
