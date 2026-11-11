using System.Text.RegularExpressions;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class FacultyDoiLookupService
{
    private static readonly Regex DoiPattern = new(
        @"^10\.\d{4,9}/\S+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IFacultyDoiRepository _repository;

    public FacultyDoiLookupService(IFacultyDoiRepository repository)
    {
        _repository = repository;
    }

    public async Task<FacultyDoiLookupResult> LookupAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = AcademicOnlineUniversalFacultyLookupService.NormalizeUvaUserId(userId);
        var rows = await _repository.GetByUserIdAsync(normalizedUserId, cancellationToken);
        var rosterRows = rows.Where(row => row.IsRosterMember).ToArray();
        var entries = rows.Select(row => new { Row = row, Doi = NormalizeDoi(row.Doi) })
            .Where(item => item.Doi is not null)
            .GroupBy(item => item.Doi!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var newest = group.OrderByDescending(item => item.Row.Year)
                    .ThenBy(item => string.IsNullOrWhiteSpace(item.Row.Title))
                    .ThenBy(item => item.Row.Title, StringComparer.OrdinalIgnoreCase).First();
                var doi = group.Key;
                return new FacultyDoiEntry(
                    doi,
                    "https://doi.org/" + string.Join("/", doi.Split('/').Select(Uri.EscapeDataString)),
                    AcademicOnlineTextCleaner.ToPlainText(newest.Row.Title, "(Title not supplied)"),
                    newest.Row.Year,
                    group.Select(item => item.Row.Source).Distinct(StringComparer.Ordinal)
                        .OrderBy(source => source, StringComparer.Ordinal).ToArray());
            })
            .OrderByDescending(entry => entry.Year)
            .ThenBy(entry => entry.Doi, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FacultyDoiLookupResult(
            normalizedUserId,
            (rosterRows.Length > 0 ? rosterRows : rows).Select(row => row.FacultyName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            entries, rosterRows.Length > 0)
            { InRebuildImport = rows.Any(row => row.InRebuildImport) };
    }

    public static string? NormalizeDoi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var doi = value.Trim();
        foreach (var prefix in new[]
                 {
                     "https://dx.doi.org/", "http://dx.doi.org/",
                     "https://doi.org/", "http://doi.org/", "doi:"
                 })
        {
            if (!doi.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            doi = doi[prefix.Length..].Trim();
            if (prefix != "doi:") doi = Uri.UnescapeDataString(doi);
            break;
        }

        return DoiPattern.IsMatch(doi) && !doi.Any(char.IsControl) ? doi.ToLowerInvariant() : null;
    }
}

public sealed record FacultyDoiEntry(
    string Doi, string Url, string Title, int? Year, IReadOnlyList<string> Sources);

public sealed record FacultyDoiLookupResult(
    string UserId, IReadOnlyList<string> FacultyNames, IReadOnlyList<FacultyDoiEntry> Dois,
    bool InImportedRoster = false)
{
    public bool InRebuildImport { get; init; }
    public bool FacultyFound => FacultyNames.Count > 0;
}
