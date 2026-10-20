using System.Text.RegularExpressions;

namespace SessionDesk.Api.Briefing;

public static partial class CitationCheck
{
    public static IReadOnlyList<string> Unknown(string draft, IReadOnlySet<string> allowed)
    {
        var unknown = new List<string>();
        foreach (Match match in BillNumber().Matches(draft))
        {
            var normalized = $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}";
            if (!allowed.Contains(normalized) && !unknown.Contains(normalized))
            {
                unknown.Add(normalized);
            }
        }

        return unknown;
    }

    public static string Normalize(string number)
    {
        var match = BillNumber().Match(number.Trim());
        return match.Success
            ? $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}"
            : number.Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"\b(SB|HB)\s*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BillNumber();
}
