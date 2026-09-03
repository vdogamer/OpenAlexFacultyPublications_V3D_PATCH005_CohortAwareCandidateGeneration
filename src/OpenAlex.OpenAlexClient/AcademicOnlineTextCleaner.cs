using System.Net;
using System.Text.RegularExpressions;

namespace OpenAlex.OpenAlexClient;

public static class AcademicOnlineTextCleaner
{
    private static readonly Regex HtmlTagPattern =
        new(
            "<[^>]+>",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.Singleline);

    public static string ToPlainText(
        string? value,
        string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var withoutTags = HtmlTagPattern.Replace(value, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        var normalized = string.Join(
            " ",
            decoded.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }
}
