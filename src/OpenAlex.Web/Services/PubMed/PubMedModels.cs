namespace OpenAlex.Web.Services.PubMed;

public enum PubMedSearchMode { Author, Orcid, Identifiers }

public sealed class PubMedOptions
{
    // When empty, use the existing application contact in OpenAlex:MailTo.
    public string Email { get; set; } = string.Empty;
}

public sealed record PubMedSearchInput(PubMedSearchMode Mode, string? AuthorTerms = null,
    string? Orcid = null, string? Identifiers = null, string? Affiliation = null,
    int? YearFrom = null, int? YearTo = null, int Limit = 25);

public sealed record PubMedQuery(PubMedSearchMode Mode, string Term,
    IReadOnlyList<string> AuthorTerms, string? Orcid,
    IReadOnlyList<string> RequestedIdentifiers, string? Affiliation, int Limit)
{
    public string SearchUrl => "https://pubmed.ncbi.nlm.nih.gov/?term=" + Uri.EscapeDataString(Term);
}

public sealed record PubMedContributor(string Role, string Name, string LastName, string Initials,
    IReadOnlyList<string> Orcids, IReadOnlyList<string> Affiliations);

public sealed record PubMedArticle(string Pmid, string? Pmcid, string? Doi,
    string Title, string Journal, int? Year, IReadOnlyList<string> PublicationTypes,
    IReadOnlyList<PubMedContributor> Contributors)
{
    public string PubMedUrl => "https://pubmed.ncbi.nlm.nih.gov/" + Pmid + "/";
    public string? PmcUrl => Pmcid is null ? null : "https://pmc.ncbi.nlm.nih.gov/articles/" + Pmcid + "/";
    public string? DoiUrl => Doi is null ? null : "https://doi.org/"
        + string.Join("/", Doi.Split('/').Select(Uri.EscapeDataString));
}

public sealed record PubMedSearchResponse(long TotalFound, IReadOnlyList<PubMedArticle> Articles,
    string QueryTranslation, IReadOnlyList<string> Notices, DateTimeOffset RetrievedAtUtc);

public interface IPubMedClient
{
    Task<PubMedSearchResponse> SearchAsync(PubMedQuery query, CancellationToken cancellationToken = default);
}

public sealed record PubMedFacultyContext(string UserId, IReadOnlyList<string> Names,
    IReadOnlyList<string> Departments, IReadOnlyList<string> StoredOrcids);

public sealed record PubMedContributorEvidence(PubMedContributor Contributor,
    bool NameCompatible, bool OrcidMatches, bool AffiliationContainsFilter);

public sealed record PubMedCandidate(PubMedArticle Article,
    IReadOnlyList<PubMedContributorEvidence> ContributorEvidence, int TotalContributorCount);

public sealed record PubMedCandidateResult(PubMedQuery Query, long TotalFound,
    IReadOnlyList<PubMedCandidate> Candidates, string QueryTranslation,
    IReadOnlyList<string> Notices, DateTimeOffset RetrievedAtUtc);

public sealed class PubMedInputException : Exception
{
    public PubMedInputException(string message) : base(message) { }
}

public sealed class PubMedProviderException : Exception
{
    public PubMedProviderException(string message, Exception? inner = null) : base(message, inner) { }
}
