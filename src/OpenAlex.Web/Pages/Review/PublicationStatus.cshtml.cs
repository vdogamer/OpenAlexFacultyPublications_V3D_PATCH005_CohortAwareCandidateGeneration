using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class PublicationStatusModel : PageModel
{
    private readonly ReviewQueueRepository _repository;
    private readonly DepartmentReviewAuthorizationService _departmentAuthorization;

    public PublicationStatusModel(
        ReviewQueueRepository repository,
        DepartmentReviewAuthorizationService departmentAuthorization)
    {
        _repository = repository;
        _departmentAuthorization = departmentAuthorization;
    }

    [BindProperty(SupportsGet = true)]
    public long DepartmentId { get; set; } = 2;

    public string DepartmentName { get; private set; }
        = "Department of Cell Biology";

    public CurrentPublicationReviewStatusSummary Summary
        { get; private set; }
        = new();

    public IReadOnlyList<CurrentPublicationReviewStatusItem> Items
        { get; private set; }
        = Array.Empty<CurrentPublicationReviewStatusItem>();

    public IReadOnlyList<CurrentPublicationReviewDecisionHistoryItem>
        DecisionHistory
        { get; private set; }
        = Array.Empty<CurrentPublicationReviewDecisionHistoryItem>();

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "Any";

    [BindProperty(SupportsGet = true)]
    public string? SearchText { get; set; }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText)
        || !string.Equals(
            StatusFilter,
            "Any",
            StringComparison.OrdinalIgnoreCase);

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

        var department =
            departments.FirstOrDefault(
                d => d.DepartmentId == DepartmentId);

        if (department is null)
        {
            return NotFound();
        }

        DepartmentName =
            department.DepartmentName;

        Summary =
            await _repository
                .GetCurrentPublicationStatusSummaryAsync(
                    DepartmentId,
                    cancellationToken);

        Items =
            await _repository
                .GetCurrentPublicationStatusItemsAsync(
                    DepartmentId,
                    200,
                    StatusFilter,
                    SearchText,
                    cancellationToken);

        DecisionHistory =
            await _repository
                .GetCurrentPublicationDecisionHistoryAsync(
                    DepartmentId,
                    100,
                    cancellationToken);

        return Page();
    }
}
