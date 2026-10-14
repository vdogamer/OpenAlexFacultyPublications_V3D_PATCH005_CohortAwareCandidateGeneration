using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class PublicationsModel : PageModel
{
    private readonly ReviewQueueRepository _repository;
    private readonly ReviewDecisionRepository _decisionRepository;
    private readonly DepartmentReviewAuthorizationService _departmentAuthorization;

    public PublicationsModel(
        ReviewQueueRepository repository,
        ReviewDecisionRepository decisionRepository,
        DepartmentReviewAuthorizationService departmentAuthorization)
    {
        _repository = repository;
        _decisionRepository = decisionRepository;
        _departmentAuthorization = departmentAuthorization;
    }

    [BindProperty(SupportsGet = true)]
    public long DepartmentId { get; set; } = 2;

    [BindProperty(SupportsGet = true)]
    public string? SearchText { get; set; }

    public IReadOnlyList<CurrentPublicationReviewQueueItem> Items
        { get; private set; }
        = Array.Empty<CurrentPublicationReviewQueueItem>();

    public string DepartmentName { get; private set; }
        = "Department of Cell Biology";

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText);


    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!await _departmentAuthorization
                .CanReviewDepartmentAsync(
                    User,
                    DepartmentId,
                    cancellationToken))
        {
            return Forbid();
        }

        var departments =
            await _repository
                .GetReviewDepartmentOptionsAsync(
                    cancellationToken);

        DepartmentName =
            departments
                .FirstOrDefault(
                    d => d.DepartmentId == DepartmentId)
                ?.DepartmentName
            ?? $"Department {DepartmentId}";

        Items =
            await _repository
                .GetCurrentPublicationQueueAsync(
                    DepartmentId,
                    200,
                    SearchText,
                    cancellationToken);

        return Page();
    }


    public async Task<IActionResult> OnPostAcceptAsync(
        long id,
        long departmentId,
        string? notes,
        string? searchText,
        CancellationToken cancellationToken)
    {
        if (!await _departmentAuthorization
                .CanReviewDepartmentAsync(
                    User,
                    departmentId,
                    cancellationToken))
        {
            return Forbid();
        }

        await _decisionRepository
            .AcceptCurrentPublicationCandidateAsync(
                id,
                GetReviewerName(),
                notes,
                cancellationToken);

        return RedirectToPage(
            new
            {
                DepartmentId = departmentId,
                SearchText = searchText
            });
    }


    public async Task<IActionResult> OnPostRejectAsync(
        long id,
        long departmentId,
        string? notes,
        string? searchText,
        CancellationToken cancellationToken)
    {
        if (!await _departmentAuthorization
                .CanReviewDepartmentAsync(
                    User,
                    departmentId,
                    cancellationToken))
        {
            return Forbid();
        }

        await _decisionRepository
            .RejectCurrentPublicationCandidateAsync(
                id,
                GetReviewerName(),
                notes,
                cancellationToken);

        return RedirectToPage(
            new
            {
                DepartmentId = departmentId,
                SearchText = searchText
            });
    }


    private string GetReviewerName()
    {
        return User?.Identity?.IsAuthenticated == true
               && !string.IsNullOrWhiteSpace(
                   User.Identity.Name)

            ? User.Identity.Name!

            : "local.demo.reviewer";
    }
}
