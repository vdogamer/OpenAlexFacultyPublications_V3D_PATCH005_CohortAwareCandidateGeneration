using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class FacultyModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long DepartmentId { get; set; } = 2;

    private readonly ReviewQueueRepository _repository;
    private readonly ReviewDecisionRepository _decisionRepository;
    private readonly DepartmentReviewAuthorizationService _departmentAuthorization;

    public FacultyModel(
        ReviewQueueRepository repository,
        ReviewDecisionRepository decisionRepository,
        DepartmentReviewAuthorizationService departmentAuthorization)
    {
        _repository = repository;
        _decisionRepository = decisionRepository;
        _departmentAuthorization = departmentAuthorization;
    }

    [BindProperty(SupportsGet = true)]
    public string ReportingWindowFilter { get; set; }
        = "Any";

    [BindProperty(SupportsGet = true)]
    public string FacultyKey { get; set; }
        = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; }
        = "Pending";

    [BindProperty(SupportsGet = true)]
    public int? YearFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string DoiFilter { get; set; }
        = "Any";

    [BindProperty(SupportsGet = true)]
    public string? SearchText { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; }
        = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; }
        = 25;


    public FacultyReviewWorkspaceSummary Summary
        { get; private set; }
        = new();

    public IReadOnlyList<FacultyReviewWorkspaceItem> Items
        { get; private set; }
        = Array.Empty<FacultyReviewWorkspaceItem>();

    public IReadOnlyList<int> AvailableYears
        { get; private set; }
        = Array.Empty<int>();

    public long TotalFilteredCount { get; private set; }

    public int TotalPages =>
        TotalFilteredCount == 0
            ? 1
            : (int)Math.Ceiling(
                TotalFilteredCount / (double)PageSize);


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

        if (string.IsNullOrWhiteSpace(FacultyKey))
        {
            return RedirectToPage(
                "/Review/DepartmentDashboard");
        }

        NormalizePaging();

        Summary =
            await _repository
                .GetFacultyReviewWorkspaceSummaryAsync(
                    DepartmentId,
                    FacultyKey,
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
            Summary.FacultyKey))
        {
            return NotFound();
        }

        AvailableYears =
            await _repository
                .GetFacultyReviewWorkspaceYearsAsync(
                    DepartmentId,
                    FacultyKey,
                    cancellationToken);

        var page =
            await LoadPageAsync(cancellationToken);

        TotalFilteredCount =
            page.TotalFilteredCount;

        if (PageNumber > TotalPages
            && TotalFilteredCount > 0)
        {
            PageNumber = TotalPages;

            page =
                await LoadPageAsync(cancellationToken);

            TotalFilteredCount =
                page.TotalFilteredCount;
        }

        Items = page.Items;

        return Page();
    }


    public async Task<IActionResult> OnPostAcceptAsync(
        long id,
        long departmentId,
        string facultyKey,
        string statusFilter,
        int? yearFilter,
        string doiFilter,
        string? searchText,
        int pageNumber,
        int pageSize,
        string reportingWindowFilter,
        string? notes,
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
                FacultyKey = facultyKey,
                DepartmentId = departmentId,
                StatusFilter = statusFilter,
                YearFilter = yearFilter,
                DoiFilter = doiFilter,
                SearchText = searchText,
                PageNumber = pageNumber,
                PageSize = pageSize,
                ReportingWindowFilter = reportingWindowFilter
            });
    }


    public async Task<IActionResult> OnPostRejectAsync(
        long id,
        long departmentId,
        string facultyKey,
        string statusFilter,
        int? yearFilter,
        string doiFilter,
        string? searchText,
        int pageNumber,
        int pageSize,
        string? notes,
        string reportingWindowFilter,
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
                FacultyKey = facultyKey,
                DepartmentId = departmentId,
                StatusFilter = statusFilter,
                YearFilter = yearFilter,
                DoiFilter = doiFilter,
                SearchText = searchText,
                PageNumber = pageNumber,
                ReportingWindowFilter = reportingWindowFilter,
                PageSize = pageSize
            });
    }


    private async Task<FacultyReviewWorkspacePageResult>
        LoadPageAsync(
            CancellationToken cancellationToken)
    {
        return await _repository
            .GetFacultyReviewWorkspacePageAsync(
                DepartmentId,
                FacultyKey,
                StatusFilter,
                YearFilter,
                DoiFilter,
                ReportingWindowFilter,
                SearchText,
                PageNumber,
                PageSize,
                cancellationToken);
    }


    private void NormalizePaging()
    {
        PageNumber =
            Math.Max(1, PageNumber);

        PageSize =
            PageSize switch
            {
                10 => 10,
                25 => 25,
                50 => 50,
                100 => 100,
                _ => 25
            };
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
