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

    public DataProvenanceModel(PublicationProvenanceRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchText { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DecisionStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AcademicOnlineStatus { get; set; }

    public IReadOnlyList<PublicationProvenanceDataRow> Rows { get; private set; } = Array.Empty<PublicationProvenanceDataRow>();

    public int TotalRows => Rows.Count;
    public int AcademicOnlineObservedRows => Rows.Count(row => row.AcademicOnlineObserved);
    public int AcceptedRows => Rows.Count(row => string.Equals(row.PublicationDecisionStatus, "Accepted", StringComparison.OrdinalIgnoreCase));
    public int PendingReviewRows => Rows.Count(row => string.IsNullOrWhiteSpace(row.PublicationDecisionStatus));
    public int UnknownIdentitySourceRows => Rows.Count(row => string.IsNullOrWhiteSpace(row.IdentityDecisionSource) && string.IsNullOrWhiteSpace(row.IdentityCandidateSource));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IEnumerable<PublicationProvenanceDataRow> rows = await _repository.GetRowsAsync(1500, cancellationToken);

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
        }

        Rows = rows.ToList();
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
