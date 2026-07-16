using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class DataProvenanceModel : PageModel
{
    private readonly PublicationProvenanceRepository _repository;
    private readonly PublicationProvenanceCoverageRepository _coverageRepository;

    public DataProvenanceModel(
        PublicationProvenanceRepository repository,
        PublicationProvenanceCoverageRepository coverageRepository)
    {
        _repository = repository;
        _coverageRepository = coverageRepository;
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchText { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DecisionStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AcademicOnlineStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CoverageStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ConsistencyStatus { get; set; }

    public IReadOnlyList<PublicationProvenanceDataRow> Rows { get; private set; } = Array.Empty<PublicationProvenanceDataRow>();
    public IReadOnlyList<PublicationProvenanceCoverageRow> CoverageRows { get; private set; } = Array.Empty<PublicationProvenanceCoverageRow>();

    public int TotalRows => Rows.Count;
    public int AcademicOnlineObservedRows => Rows.Count(row => row.AcademicOnlineObserved);
    public int AcceptedRows => Rows.Count(row => string.Equals(row.PublicationDecisionStatus, "Accepted", StringComparison.OrdinalIgnoreCase));
    public int PendingReviewRows => Rows.Count(row => string.IsNullOrWhiteSpace(row.PublicationDecisionStatus));
    public int UnknownIdentitySourceRows => Rows.Count(row => string.IsNullOrWhiteSpace(row.IdentityDecisionSource) && string.IsNullOrWhiteSpace(row.IdentityCandidateSource));
    public int CandidateDecisionMismatchRows => Rows.Count(HasCandidateDecisionMismatch);
    public int AcademicOnlineCoverageGapRows => CoverageRows.Count(row => !string.Equals(row.CoverageStatus, "AcademicOnlineAcceptedByUs", StringComparison.OrdinalIgnoreCase));
    public int AcademicOnlineOnlyNoLocalWorkRows => CoverageRows.Count(row => string.Equals(row.CoverageStatus, "AcademicOnlineOnly_NoLocalWork", StringComparison.OrdinalIgnoreCase));
    public int AcademicOnlineAcceptedByBothRows => CoverageRows.Count(row => string.Equals(row.CoverageStatus, "AcademicOnlineAcceptedByUs", StringComparison.OrdinalIgnoreCase));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IEnumerable<PublicationProvenanceDataRow> rows = await _repository.GetRowsAsync(1500, cancellationToken);
        IEnumerable<PublicationProvenanceCoverageRow> coverageRows = await _coverageRepository.GetRowsAsync(1500, cancellationToken);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            rows = rows.Where(row =>
                Contains(row.FacultyDisplayName, search) ||
                Contains(row.DepartmentName, search) ||
                Contains(row.WorkTitle, search) ||
                Contains(row.WorkDoi, search) ||
                Contains(row.WorkOpenAlexId, search) ||
                Contains(row.AuthorDisplayName, search) ||
                Contains(row.AcademicOnlineScholarName, search));

            coverageRows = coverageRows.Where(row =>
                Contains(row.FacultyDisplayName, search) ||
                Contains(row.DepartmentName, search) ||
                Contains(row.PublicationTitle, search) ||
                Contains(row.Doi, search) ||
                Contains(row.OpenAlexId, search) ||
                Contains(row.AcademicOnlineScholarName, search));
        }

        if (!string.IsNullOrWhiteSpace(DecisionStatus))
        {
            rows = DecisionStatus.Trim().ToLowerInvariant() switch
            {
                "accepted" => rows.Where(row => string.Equals(row.PublicationDecisionStatus, "Accepted", StringComparison.OrdinalIgnoreCase)),
                "rejected" => rows.Where(row => string.Equals(row.PublicationDecisionStatus, "Rejected", StringComparison.OrdinalIgnoreCase)),
                "pending" => rows.Where(row => string.IsNullOrWhiteSpace(row.PublicationDecisionStatus)),
                _ => rows
            };

            coverageRows = DecisionStatus.Trim().ToLowerInvariant() switch
            {
                "accepted" => coverageRows.Where(row => string.Equals(row.LocalDecisionStatus, "Accepted", StringComparison.OrdinalIgnoreCase)),
                "rejected" => coverageRows.Where(row => string.Equals(row.LocalDecisionStatus, "Rejected", StringComparison.OrdinalIgnoreCase)),
                "pending" => coverageRows.Where(row => string.IsNullOrWhiteSpace(row.LocalDecisionStatus)),
                _ => coverageRows
            };
        }

        if (!string.IsNullOrWhiteSpace(AcademicOnlineStatus))
        {
            rows = AcademicOnlineStatus.Trim().ToLowerInvariant() switch
            {
                "observed" => rows.Where(row => row.AcademicOnlineObserved),
                "verified" => rows.Where(row => row.AcademicOnlineObserved && row.AcademicOnlineVerified == true),
                "not-observed" => rows.Where(row => !row.AcademicOnlineObserved),
                _ => rows
            };

            coverageRows = AcademicOnlineStatus.Trim().ToLowerInvariant() switch
            {
                "observed" => coverageRows,
                "verified" => coverageRows.Where(row => row.AcademicOnlineVerified == true),
                "not-observed" => Enumerable.Empty<PublicationProvenanceCoverageRow>(),
                _ => coverageRows
            };
        }

        if (!string.IsNullOrWhiteSpace(CoverageStatus))
        {
            var coverageFilter = CoverageStatus.Trim();
            coverageRows = coverageRows.Where(row => string.Equals(row.CoverageStatus, coverageFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ConsistencyStatus))
        {
            rows = ConsistencyStatus.Trim().ToLowerInvariant() switch
            {
                "mismatch" => rows.Where(HasCandidateDecisionMismatch),
                "consistent" => rows.Where(row => !HasCandidateDecisionMismatch(row)),
                _ => rows
            };
        }

        Rows = rows.ToList();
        CoverageRows = coverageRows.ToList();
    }

    public static bool HasCandidateDecisionMismatch(PublicationProvenanceDataRow row)
    {
        if (string.IsNullOrWhiteSpace(row.PublicationDecisionStatus))
        {
            return false;
        }

        if (string.Equals(row.PublicationDecisionStatus, "Accepted", StringComparison.OrdinalIgnoreCase))
        {
            return !IsAcceptedCandidateStatus(row.PublicationCandidateStatus);
        }

        if (string.Equals(row.PublicationDecisionStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return !IsRejectedCandidateStatus(row.PublicationCandidateStatus);
        }

        return false;
    }

    public static string GetStatusConsistencyExplanation(PublicationProvenanceDataRow row)
    {
        if (!HasCandidateDecisionMismatch(row))
        {
            return "Candidate status and current publication decision are consistent.";
        }

        return $"Candidate status '{ValueOrUnknown(row.PublicationCandidateStatus)}' does not agree with the current UVA decision '{ValueOrUnknown(row.PublicationDecisionStatus)}'. The current decision remains authoritative.";
    }

    private static bool IsAcceptedCandidateStatus(string? value)
    {
        return string.Equals(value, "AutoAcceptedCandidate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "AcceptedCandidate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Accepted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRejectedCandidateStatus(string? value)
    {
        return string.Equals(value, "AutoRejectedCandidate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "RejectedCandidate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string ValueOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }
}
