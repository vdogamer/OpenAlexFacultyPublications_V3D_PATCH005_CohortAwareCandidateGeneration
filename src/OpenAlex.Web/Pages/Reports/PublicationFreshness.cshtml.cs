using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Auth;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class PublicationFreshnessModel : PageModel
{
    private readonly FacultyPublicationFreshnessService _service;
    private readonly DepartmentReviewAuthorizationService _departmentReviewAuthorization;

    public PublicationFreshnessModel(
        FacultyPublicationFreshnessService service,
        DepartmentReviewAuthorizationService departmentReviewAuthorization)
    {
        _service = service;
        _departmentReviewAuthorization = departmentReviewAuthorization;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Doi { get; set; }

    public FacultyPublicationFreshnessResult? Result { get; private set; }

    public PublicationDoiLookupResult? DoiResult { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? DoiErrorMessage { get; private set; }

    public string? ReviewActionMessage { get; private set; }

    public string? ReviewActionErrorMessage { get; private set; }

    public ISet<long> AuthorizedReviewDepartmentIds { get; } =
        new HashSet<long>();

    public bool HasReviewerRole =>
        User.IsInRole("Reviewer") ||
        User.IsInRole("Admin");

    public bool CanCurrentUserSendToReview(
        PublicationDoiFacultyCandidate candidate)
    {
        return candidate.CanSendToReview &&
               candidate.ReviewDepartmentId.HasValue &&
               AuthorizedReviewDepartmentIds.Contains(
                   candidate.ReviewDepartmentId.Value);
    }

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        ReviewActionMessage =
            TempData["PublicationFreshnessReviewMessage"] as string;

        ReviewActionErrorMessage =
            TempData["PublicationFreshnessReviewError"] as string;

        if (!string.IsNullOrWhiteSpace(Doi))
        {
            try
            {
                DoiResult =
                    await _service.LookupDoiAsync(
                        Doi,
                        cancellationToken);

                Doi = DoiResult.Doi;

                await LoadDoiReviewAuthorizationsAsync(
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DoiErrorMessage = ex.Message;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(UserId))
        {
            return;
        }

        try
        {
            Result =
                await _service.ScanByUserIdAsync(
                    UserId,
                    FromYear,
                    cancellationToken);

            UserId = Result.UvaUserId;
            FromYear = Result.FromYear;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<IActionResult> OnPostSendToReviewAsync(
        string doi,
        string uvaUserId,
        long departmentId,
        CancellationToken cancellationToken)
    {
        if (!HasReviewerRole)
        {
            return Forbid();
        }

        if (!await _departmentReviewAuthorization
                .CanReviewDepartmentAsync(
                    User,
                    departmentId,
                    cancellationToken))
        {
            return Forbid();
        }

        try
        {
            var submittedBy =
                User?.Identity?.IsAuthenticated == true &&
                !string.IsNullOrWhiteSpace(User.Identity.Name)
                    ? User.Identity.Name!
                    : "local.demo.reviewer";

            var result =
                await _service.SendDoiToReviewAsync(
                    doi,
                    uvaUserId,
                    departmentId,
                    submittedBy,
                    cancellationToken);

            TempData["PublicationFreshnessReviewMessage"] =
                result.WasCreated
                    ? $"Publication sent to review. Candidate #{result.FacultyPublicationReviewCandidateId} was created."
                    : $"This faculty/publication pair is already in the review workflow as candidate #{result.FacultyPublicationReviewCandidateId}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["PublicationFreshnessReviewError"] =
                ex.Message;
        }

        return RedirectToPage(
            new
            {
                Doi = doi
            });
    }

    private async Task LoadDoiReviewAuthorizationsAsync(
        CancellationToken cancellationToken)
    {
        if (DoiResult is null || !HasReviewerRole)
        {
            return;
        }

        var departmentIds = DoiResult.FacultyCandidates
            .Where(candidate =>
                candidate.CanSendToReview &&
                candidate.ReviewDepartmentId.HasValue)
            .Select(candidate =>
                candidate.ReviewDepartmentId!.Value)
            .Distinct()
            .ToArray();

        foreach (var departmentId in departmentIds)
        {
            if (await _departmentReviewAuthorization
                    .CanReviewDepartmentAsync(
                        User,
                        departmentId,
                        cancellationToken))
            {
                AuthorizedReviewDepartmentIds.Add(
                    departmentId);
            }
        }
    }

}
