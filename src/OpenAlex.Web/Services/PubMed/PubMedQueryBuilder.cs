using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenAlex.Web.Services.PubMed;

public static class PubMedQueryBuilder
{
    private static readonly Regex AuthorPattern = new(@"^[\p{L}\p{M}\s.'’,\-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex IdentifierPattern = new(@"^(?:PMC)?[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant);

    public static PubMedQuery Build(PubMedSearchInput input)
    {
        if (input.Limit is not (10 or 25 or 50))
            throw new PubMedInputException("Choose a result limit of 10, 25, or 50.");
        var orcid = string.IsNullOrWhiteSpace(input.Orcid) ? null : NormalizeOrcid(input.Orcid)
            ?? throw new PubMedInputException("Enter a valid ORCID, including its check digit.");
        var authors = new List<string>();
        var identifiers = new List<string>();
        string term;
        switch (input.Mode)
        {
            case PubMedSearchMode.Author:
                if (input.AuthorTerms?.Length > 600)
                    throw new PubMedInputException("Keep the author terms within 600 characters.");
                authors = (input.AuthorTerms ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(CleanSpaces).Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (authors.Count is < 1 or > 5 || authors.Any(value => value.Length > 120 || !AuthorPattern.IsMatch(value)))
                    throw new PubMedInputException("Enter one to five author-name variants, separated by semicolons. Use surname and initials without quotes or search operators.");
                term = "(" + string.Join(" OR ", authors.Select(value => "\"" + value + "\"[au]")) + ")";
                break;
            case PubMedSearchMode.Orcid:
                if (orcid is null) throw new PubMedInputException("Supply the faculty ORCID for an ORCID search.");
                term = "\"" + orcid + "\"[auid]";
                break;
            case PubMedSearchMode.Identifiers:
                if (input.Identifiers?.Length > 500)
                    throw new PubMedInputException("Keep the identifier list within 500 characters.");
                identifiers = Regex.Split(input.Identifiers?.Trim() ?? string.Empty, @"[\s,;]+")
                    .Where(value => value.Length > 0).Select(value => value.ToUpperInvariant())
                    .Distinct(StringComparer.Ordinal).ToList();
                if (identifiers.Count is < 1 or > 10 || identifiers.Any(value => !IdentifierPattern.IsMatch(value)))
                    throw new PubMedInputException("Enter up to ten PMIDs or unversioned PMCIDs, such as 12345678 or PMC1234567.");
                term = "(" + string.Join(" OR ", identifiers.Select(value => value.StartsWith("PMC", StringComparison.Ordinal)
                    ? value : value + "[pmid]")) + ")";
                break;
            default:
                throw new PubMedInputException("Choose a supported PubMed search mode.");
        }

        string? affiliation = null;
        if (input.Mode != PubMedSearchMode.Identifiers)
        {
            affiliation = CleanSpaces(input.Affiliation ?? string.Empty);
            if (affiliation.Length > 160 || affiliation.Any(char.IsControl)
                || affiliation.IndexOfAny(new[] { '"', '[', ']', '(', ')', '*', '\\' }) >= 0)
                throw new PubMedInputException("Use a plain affiliation phrase within 160 characters, without search syntax.");
            if (affiliation.Length > 0) term += " AND \"" + affiliation + "\"[ad]";
            else affiliation = null;
            if (input.YearFrom is < 1000 or > 9999 || input.YearTo is < 1000 or > 9999
                || (input.YearFrom.HasValue && input.YearTo.HasValue && input.YearFrom.Value > input.YearTo.Value))
                throw new PubMedInputException("Use four-digit publication years with the start year no later than the end year.");
            if (input.YearFrom is not null || input.YearTo is not null)
                term += " AND (" + (input.YearFrom ?? 1000).ToString(CultureInfo.InvariantCulture)
                    + ":" + (input.YearTo ?? 9999).ToString(CultureInfo.InvariantCulture) + "[dp])";
        }
        return new PubMedQuery(input.Mode, term, authors, orcid, identifiers, affiliation, input.Limit);
    }

    public static string? NormalizeOrcid(string? value)
    {
        var id = value?.Trim().ToUpperInvariant();
        foreach (var prefix in new[] { "HTTPS://ORCID.ORG/", "HTTP://ORCID.ORG/" })
            if (id?.StartsWith(prefix, StringComparison.Ordinal) == true) { id = id[prefix.Length..]; break; }
        if (id is null || !Regex.IsMatch(id, @"^[0-9]{4}-[0-9]{4}-[0-9]{4}-[0-9]{3}[0-9X]$")) return null;
        var compact = id.Replace("-", string.Empty);
        var total = 0;
        for (var i = 0; i < 15; i++) total = (total + compact[i] - '0') * 2;
        var check = (12 - total % 11) % 11;
        return compact[15] == (check == 10 ? 'X' : (char)('0' + check)) ? id : null;
    }

    public static string SuggestAuthorTerms(IEnumerable<string> names)
    {
        // Suggestions are editable search hypotheses, not verified name parsing.
        var suggestions = new List<string>();
        foreach (var name in names)
        {
            var parts = CleanSpaces(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !char.IsLetter(parts[0][0])) continue;
            suggestions.Add(parts[^1] + " " + parts[0][0]);
            suggestions.Add(parts[^1] + " " + string.Concat(parts[..^1].Where(part => char.IsLetter(part[0])).Select(part => part[0])));
        }
        return string.Join("; ", suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
    }

    private static string CleanSpaces(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
}
