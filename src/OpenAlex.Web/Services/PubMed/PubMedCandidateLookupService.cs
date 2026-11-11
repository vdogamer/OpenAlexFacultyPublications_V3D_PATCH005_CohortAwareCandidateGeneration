using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenAlex.Data;

namespace OpenAlex.Web.Services.PubMed;

public sealed class PubMedCandidateLookupService
{
    private readonly IFacultyIdentityLookup _faculty;
    private readonly IPubMedClient _pubmed;

    public PubMedCandidateLookupService(IFacultyIdentityLookup faculty, IPubMedClient pubmed)
    {
        _faculty = faculty; _pubmed = pubmed;
    }

    public async Task<PubMedFacultyContext?> ResolveFacultyAsync(string userId, CancellationToken token = default)
    {
        string normalized;
        try { normalized = AcademicOnlineUniversalFacultyLookupService.NormalizeUvaUserId(userId); }
        catch (InvalidOperationException)
        { throw new PubMedInputException("Enter a valid UVA ComputingID or its @virginia.edu email address."); }
        var rows = await _faculty.GetByComputingIdAsync(normalized, token);
        if (rows.Count == 0) return null;
        if (rows.Where(row => row.FacultyId is not null).Select(row => row.FacultyId).Distinct().Count() > 1)
            throw new PubMedInputException("Several native faculty records share this ComputingID. Review the directory identity before searching.");
        return new PubMedFacultyContext(normalized,
            rows.SelectMany(row => new[] { row.DisplayName, row.ImportedDisplayName })
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            rows.SelectMany(row => new[] { row.DepartmentName, row.ImportedDepartmentName })
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            rows.Select(row => PubMedQueryBuilder.NormalizeOrcid(row.Orcid)).Where(value => value is not null)
                .Select(value => value!).Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<PubMedCandidateResult> SearchAsync(PubMedFacultyContext faculty, PubMedQuery query,
        CancellationToken token = default)
    {
        var response = await _pubmed.SearchAsync(query, token);
        var names = faculty.Names.Concat(query.AuthorTerms).Select(NameKey).ToHashSet(StringComparer.Ordinal);
        var candidates = response.Articles.Select(article =>
        {
            var evidence = article.Contributors.Select(contributor =>
                new PubMedContributorEvidence(contributor,
                    names.Contains(NameKey(contributor.Name))
                        || names.Contains(NameKey(contributor.LastName + " " + contributor.Initials)),
                    query.Orcid is not null && contributor.Orcids.Contains(query.Orcid, StringComparer.Ordinal),
                    query.Affiliation is not null && contributor.Affiliations.Any(affiliation =>
                        affiliation.Contains(query.Affiliation, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(row => row.OrcidMatches).ThenByDescending(row => row.NameCompatible)
                .ThenByDescending(row => row.AffiliationContainsFilter).Take(20).ToArray();
            return new PubMedCandidate(article, evidence, article.Contributors.Count);
        }).ToArray();
        return new PubMedCandidateResult(query, response.TotalFound, candidates, response.QueryTranslation,
            response.Notices, response.RetrievedAtUtc);
    }

    private static string NameKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var letters = new string(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(letters, @"[^\p{L}\p{Nd}]", string.Empty).ToLowerInvariant();
    }
}
