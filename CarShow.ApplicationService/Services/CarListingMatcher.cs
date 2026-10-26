using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;

namespace CarShow.ApplicationService.Services;

public static class CarListingMatcher
{
    public static string Normalize(string value)
    {
        var text = new StringBuilder();
        foreach (var ch in value.ToLowerInvariant())
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(ch);
            text.Append(digit >= 0 ? (char)('0' + digit) : ch switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                '\u200c' => ' ',
                _ => ch
            });
        }

        return Regex.Replace(text.ToString(), @"\s+", " ").Trim();
    }

    public static DateOnly TehranDay(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
    }

    public static void Match(CarListing listing, IReadOnlyList<Car> catalog)
    {
        listing.CarId = null;
        listing.NormalizedPrice = null;
        listing.MatchStatus = "Invalid";
        listing.ProcessingStatus = "Processing";
        if (listing.Price is null or <= 0 || listing.Currency is not ("IRT" or "IRR"))
        {
            listing.MatchReason = "Price or explicit currency is missing or invalid.";
            listing.ProcessingStatus = "Rejected";
            return;
        }

        if (listing.IsUsed is null)
        {
            listing.MatchReason = "New/used condition must be explicit (zero mileage, used, or mileage).";
            listing.ProcessingStatus = "Rejected";
            return;
        }

        var text = Normalize(listing.RawText);
        var identities = catalog.Where(car => !car.IsDelete
            && Contains(text, car.Company.Name)
            && Contains(text, car.CarModel.Name)
            && (car.TipId != null
                ? Contains(text, car.Tip?.Name ?? "")
                : Contains(text, car.Name) && !catalog.Any(other => other.CompanyId == car.CompanyId
                    && other.CarModelId == car.CarModelId && other.TipId != null && !other.IsDelete))).ToList();
        // Prefer the fully specified trim over a generic catalog row of the same model.
        if (identities.Any(car => car.TipId != null))
        {
            identities = identities.Where(car => car.TipId != null).ToList();
        }

        var candidates = identities.Where(car => car.IsUsed == listing.IsUsed).ToList();
        listing.MatchStatus = candidates.Count switch
        {
            0 => "Unmatched",
            1 => "Matched",
            _ => "Ambiguous"
        };
        listing.MatchReason = candidates.Count == 1
            ? "Exact manufacturer, model, trim and condition tokens."
            : $"{candidates.Count} catalog candidates; manufacturer/model/trim could not be uniquely verified.";
        listing.ProcessingStatus = candidates.Count == 1 ? "Compared" : "Rejected";
        if (candidates.Count != 1)
        {
            return;
        }

        listing.CarId = candidates[0].Id;
        listing.Brand = candidates[0].Company.Name;
        listing.Model = candidates[0].CarModel.Name;
        listing.Trim = candidates[0].Tip?.Name;
        listing.NormalizedPrice = listing.Currency == "IRR" ? listing.Price / 10m : listing.Price;
        listing.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool Contains(string text, string name)
    {
        var token = Normalize(name);
        return token.Length > 0 && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(token)}(?![\p{{L}}\p{{N}}])");
    }
}
