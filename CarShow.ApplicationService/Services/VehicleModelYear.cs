using System.Text.RegularExpressions;

namespace CarShow.ApplicationService.Services;

/// <summary>Only reads explicit or unambiguous model-year tokens; never converts calendars.</summary>
public static class VehicleModelYear
{
    public static (int? Year, string? Calendar, bool Ambiguous) Read(string normalizedDigits)
    {
        var body = Regex.Replace(normalizedDigits,
            @"(?:بیمه(?:\s*(?:بدنه|شخص\s*ثالث))?|اعتبار|معاینه\s*فنی)\s*(?:(?:تا|مدت|سال|[:：])\s*)*(?:(?:فروردین|اردیبهشت|خرداد|تیر|مرداد|شهریور|مهر|آبان|آذر|دی|بهمن|اسفند)\s*)?[0-9/\-]+", " ");
        var labeled = Regex.Matches(body, @"(?:مدل|سال|year)\s*[:：]?\s*([0-9]{4})(?![0-9])", RegexOptions.IgnoreCase);
        var years = labeled.Select(x => x.Groups[1].Value).ToArray();
        if (years.Length == 0)
        {
            years = Regex.Matches(body, @"(?<![0-9])(?:13[0-9]{2}|14[0-9]{2}|19[0-9]{2}|20[0-9]{2})(?![0-9])(?!(?:\s*(?:میلیون|میلیارد|تومان|تومن|ریال|هزار|کیلومتر|km|م(?![\p{L}]))))", RegexOptions.IgnoreCase)
                .Select(x => x.Value).ToArray();
        }
        var valid = years.Select(int.Parse).Where(x => x is >= 1300 and <= 1500 or >= 1900 and <= 2100).Distinct().ToArray();
        if (valid.Length != 1) return (null, null, valid.Length > 1);
        return (valid[0], valid[0] <= 1500 ? "Jalali" : "Gregorian", false);
    }
}
