using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class DepartmentDashboardModel : PageModel
{
    private readonly ReviewQueueRepository _repository;
    private readonly DepartmentReviewAuthorizationService _departmentAuthorization;

    public DepartmentDashboardModel(
        ReviewQueueRepository repository,
        DepartmentReviewAuthorizationService
            departmentAuthorization)
    {
        _repository = repository;
        _departmentAuthorization =
            departmentAuthorization;
    }

    [BindProperty(SupportsGet = true)]
    public long DepartmentId { get; set; } = 2;

    public DepartmentReviewDashboardSummary Summary
        { get; private set; }
        = new();

    public IReadOnlyList<DepartmentFacultyReviewSummary>
        Faculty
        { get; private set; }
        = Array.Empty<DepartmentFacultyReviewSummary>();

    public IReadOnlyList<ReviewDepartmentOption>
        Departments
        { get; private set; }
        = Array.Empty<ReviewDepartmentOption>();

    public async Task<IActionResult> OnGetAsync(
    CancellationToken cancellationToken)
    {
        var allDepartments =
            await _repository
                .GetReviewDepartmentOptionsAsync(
                    cancellationToken);

        if (await _departmentAuthorization
            .IsGlobalAdminAsync(User, cancellationToken))
        {
            Departments =
                allDepartments;
        }
        else
        {
            var allowed =
                await _departmentAuthorization
                    .GetAllowedDepartmentsAsync(
                        User,
                        cancellationToken);

            var allowedIds =
                allowed
                    .Where(x => x.CanReview)
                    .Select(x => x.DepartmentId)
                    .ToHashSet();

            Departments =
                allDepartments
                    .Where(
                        x =>
                            allowedIds.Contains(
                                x.DepartmentId))
                    .ToArray();
        }


        if (Departments.Count == 0)
        {
            return Forbid();
        }


        var departmentWasRequested =
            Request.Query.ContainsKey(
                nameof(DepartmentId));

        if (!departmentWasRequested)
        {
            DepartmentId =
                Departments[0].DepartmentId;
        }
        else if (!Departments.Any(
                    x =>
                        x.DepartmentId ==
                        DepartmentId))
        {
            return Forbid();
        }


        if (!await _departmentAuthorization
                .CanReviewDepartmentAsync(
                    User,
                    DepartmentId,
                    cancellationToken))
        {
            return Forbid();
        }


        Summary =
            await _repository
                .GetDepartmentReviewDashboardSummaryAsync(
                    DepartmentId,
                    cancellationToken);

        Faculty =
            await _repository
                .GetDepartmentFacultyReviewSummaryAsync(
                    DepartmentId,
                    cancellationToken);


        if (string.IsNullOrWhiteSpace(
                Summary.DepartmentName))
        {
            var department =
                Departments.FirstOrDefault(
                    d =>
                        d.DepartmentId ==
                        DepartmentId);

            Summary.DepartmentId =
                DepartmentId;

            Summary.DepartmentName =
                department?.DepartmentName
                ?? $"Department {DepartmentId}";
        }


        return Page();
    }
}
