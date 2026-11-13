using System.Net;
using System.Text.RegularExpressions;

namespace CarShow.ApplicationService.Import;

internal sealed record CatalogEntry(
    string Company, string Brand, string Model, string Tip,
    string SourceUrl, bool Verified);

internal static partial class CatalogHtmlParser
{
    // This parser intentionally targets the versioned CarShow HTML catalog table,
    // not arbitrary HTML documents.
    [GeneratedRegex("<tbody\\b[^>]*id=[\"']catalogRows[\"'][^>]*>(?<body>.*?)</tbody>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BodyPattern();

    [GeneratedRegex("<tr\\b(?<attrs>[^>]*)>(?<body>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowPattern();

    [GeneratedRegex("<td\\b[^>]*>(?<body>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex("data-class=[\"'](?<value>[^\\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex("href=[\"'](?<value>[^\\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    public static IReadOnlyList<CatalogEntry> Parse(string html)
    {
        var tbody = BodyPattern().Match(html);
        if (!tbody.Success)
            throw new InvalidDataException("The catalogRows table body was not found.");

        var entries = new List<CatalogEntry>();
        foreach (Match row in RowPattern().Matches(tbody.Groups["body"].Value))
        {
            var cells = CellPattern().Matches(row.Groups["body"].Value);
            if (cells.Count != 7)
                throw new InvalidDataException("Unexpected catalog column count.");

            static string Value(Match cell) =>
                WebUtility.HtmlDecode(TagPattern().Replace(cell.Groups["body"].Value, " "))
                    .Replace('ي', 'ی').Replace('ك', 'ک').Trim();

            var status = StatusPattern().Match(row.Groups["attrs"].Value).Groups["value"].Value;
            var url = LinkPattern().Match(cells[6].Groups["body"].Value).Groups["value"].Value;
            entries.Add(new CatalogEntry(
                Value(cells[1]), Value(cells[2]), Value(cells[3]), Value(cells[4]),
                WebUtility.HtmlDecode(url), status.Equals("verified", StringComparison.OrdinalIgnoreCase)));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The catalog contains no vehicle rows.");

        return entries;
    }
}
