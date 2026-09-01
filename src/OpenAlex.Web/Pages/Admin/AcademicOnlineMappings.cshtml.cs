using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class AcademicOnlineMappingsModel : PageModel
{
    private readonly FacultyAcademicOnlineMappingRepository _repository;

    public AcademicOnlineMappingsModel(
        FacultyAcademicOnlineMappingRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public long FacultyId { get; set; } = 16;

    [BindProperty]
    public string? AcademicOnlineAuthorId { get; set; }

    [BindProperty]
    public string? AcademicOnlineName { get; set; }

    [BindProperty]
    public string? AcademicOnlineEmail { get; set; }

    [BindProperty]
    public string? AcademicOnlineOrcidUrl { get; set; }

    [BindProperty]
    public string? EvidenceNotes { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusMessage { get; set; }

    public FacultyAcademicOnlineMappingRow? Faculty { get; private set; }
    public FacultyAcademicOnlineSnapshotCandidate? LatestSnapshot { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        if (Faculty?.HasCurrentMapping == true)
        {
            AcademicOnlineAuthorId = Faculty.AcademicOnlineAuthorId;
            AcademicOnlineName = Faculty.AcademicOnlineName;
            AcademicOnlineEmail = Faculty.AcademicOnlineEmail;
            AcademicOnlineOrcidUrl = Faculty.AcademicOnlineOrcidUrl;
            EvidenceNotes = Faculty.EvidenceNotes;
        }
        else if (LatestSnapshot is not null)
        {
            AcademicOnlineAuthorId = LatestSnapshot.AcademicOnlineAuthorId;
            AcademicOnlineName = LatestSnapshot.AcademicOnlineName;
            AcademicOnlineEmail = LatestSnapshot.AcademicOnlineEmail;
            AcademicOnlineOrcidUrl = LatestSnapshot.AcademicOnlineOrcidUrl;
            EvidenceNotes =
                $"Proposed from AcademicOnlineScholarSnapshotId {LatestSnapshot.AcademicOnlineScholarSnapshotId}. Review before saving.";
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SetCurrentMappingAsync(
                FacultyId,
                AcademicOnlineAuthorId ?? string.Empty,
                AcademicOnlineName,
                AcademicOnlineEmail,
                AcademicOnlineOrcidUrl,
                "AdminVerified",
                EvidenceNotes,
                GetReviewerName(),
                cancellationToken);

            return RedirectToPage(new
            {
                FacultyId,
                StatusMessage = "Academic Online mapping saved and marked current."
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPromoteLatestSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _repository.GetLatestSnapshotCandidateAsync(
                FacultyId,
                cancellationToken);

            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    "No Academic Online scholar snapshot is available for this faculty member.");
            }

            await _repository.SetCurrentMappingAsync(
                FacultyId,
                snapshot.AcademicOnlineAuthorId,
                snapshot.AcademicOnlineName,
                snapshot.AcademicOnlineEmail,
                snapshot.AcademicOnlineOrcidUrl,
                "AdminPromotedSnapshot",
                $"Promoted from AcademicOnlineScholarSnapshotId {snapshot.AcademicOnlineScholarSnapshotId}.",
                GetReviewerName(),
                cancellationToken);

            return RedirectToPage(new
            {
                FacultyId,
                StatusMessage = "Latest Academic Online snapshot promoted to the current verified mapping."
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Faculty = await _repository.GetCurrentMappingAsync(
                FacultyId,
                cancellationToken);

            LatestSnapshot = await _repository.GetLatestSnapshotCandidateAsync(
                FacultyId,
                cancellationToken);

            if (Faculty is null)
            {
                ErrorMessage = $"FacultyId {FacultyId} was not found.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private string GetReviewerName()
    {
        return User?.Identity?.IsAuthenticated == true &&
               !string.IsNullOrWhiteSpace(User.Identity.Name)
            ? User.Identity.Name!
            : "local.demo.admin";
    }
}
