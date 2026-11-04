using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Pages;

public sealed class FacultyModel : PageModel
{
    private readonly FacultyRepository _repository;
    private readonly DepartmentReviewAuthorizationService _departmentAuthorization;

    public FacultyModel(
        FacultyRepository repository,
        DepartmentReviewAuthorizationService departmentAuthorization)
    {
        _repository = repository;
        _departmentAuthorization = departmentAuthorization;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "name";

    [BindProperty(SupportsGet = true)]
    public string Direction { get; set; } = "asc";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;


    public IReadOnlyList<FacultyListItem> Faculty
    {
        get;
        private set;
    } =
        Array.Empty<FacultyListItem>();


    public IReadOnlyList<string> Departments
    {
        get;
        private set;
    } =
        Array.Empty<string>();


    public bool CanViewAllDepartments
    {
        get;
        private set;
    } = true;


    public int TotalCount { get; private set; }


    public int TotalPages =>
        TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                TotalCount /
                (double)PageSize);


    public bool HasPreviousPage =>
        PageNumber > 1;


    public bool HasNextPage =>
        PageNumber < TotalPages;

    public IReadOnlyList<int> VisiblePageNumbers
    {
        get
        {
            if (TotalPages <= 0)
            {
                return Array.Empty<int>();
            }

            const int radius = 2;

            var start =
                Math.Max(
                    1,
                    PageNumber - radius);

            var end =
                Math.Min(
                    TotalPages,
                    PageNumber + radius);

            /*
            * Try to keep five page numbers visible.
            */
            if (end - start < 4)
            {
                if (start == 1)
                {
                    end =
                        Math.Min(
                            TotalPages,
                            start + 4);
                }
                else if (end == TotalPages)
                {
                    start =
                        Math.Max(
                            1,
                            end - 4);
                }
            }

            return Enumerable
                .Range(
                    start,
                    end - start + 1)
                .ToArray();
        }
    }

    public DateTime? DataRefreshedAtUtc
    {
        get;
        private set;
    }

    public string RefreshLabel =>
    string.IsNullOrWhiteSpace(Department)
        ? "All departments current through"
        : "Department last refreshed";


public string? DataRefreshedEastern
{
    get
    {
        if (DataRefreshedAtUtc is null)
        {
            return null;
        }

        var utc =
            DateTime.SpecifyKind(
                DataRefreshedAtUtc.Value,
                DateTimeKind.Utc);

        TimeZoneInfo eastern;

        try
        {
            eastern =
                TimeZoneInfo.FindSystemTimeZoneById(
                    "Eastern Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            eastern =
                TimeZoneInfo.FindSystemTimeZoneById(
                    "America/New_York");
        }

        var local =
            TimeZoneInfo.ConvertTimeFromUtc(
                utc,
                eastern);

        return local.ToString(
            "MMM d, yyyy h:mm tt");
    }
}

    public int StartItem =>
        TotalCount == 0
            ? 0
            : ((PageNumber - 1) * PageSize) + 1;


    public int EndItem =>
        Math.Min(
            PageNumber * PageSize,
            TotalCount);


    public string NextDirection(
        string column)
    {
        if (string.Equals(
                Sort,
                column,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                Direction,
                "asc",
                StringComparison.OrdinalIgnoreCase)
                    ? "desc"
                    : "asc";
        }

        return "asc";
    }


    public string SortIndicator(
        string column)
    {
        if (!string.Equals(
                Sort,
                column,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Equals(
            Direction,
            "desc",
            StringComparison.OrdinalIgnoreCase)
                ? "▼"
                : "▲";
    }


    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        Sort =
            Sort.Trim().ToLowerInvariant() switch
            {
                "userid" => "userid",
                "department" => "department",
                "title" => "title",
                "publications" => "publications",
                _ => "name"
            };

        Direction =
            string.Equals(
                Direction,
                "desc",
                StringComparison.OrdinalIgnoreCase)
                ? "desc"
                : "asc";

        PageNumber =
            Math.Max(1, PageNumber);

        PageSize =
            PageSize switch
            {
                50 => 50,
                100 => 100,
                _ => 25
            };

        var allDepartments =
            await _repository
                .GetFacultyDepartmentsAsync(
                    cancellationToken);

        var isDepartmentScopedReviewer =
            User.IsInRole("Reviewer")
            && !await _departmentAuthorization
                .IsGlobalAdminAsync(User, cancellationToken);

        CanViewAllDepartments =
            !isDepartmentScopedReviewer;

        if (isDepartmentScopedReviewer)
        {
            var allowedDepartments =
                await _departmentAuthorization
                    .GetAllowedDepartmentsAsync(
                        User,
                        cancellationToken);

            var allowedDepartmentNames =
                allowedDepartments
                    .Where(item => item.CanReview)
                    .Select(item => item.DepartmentName)
                    .Where(name =>
                        !string.IsNullOrWhiteSpace(name))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            Departments =
                allDepartments
                    .Where(department =>
                        allowedDepartmentNames.Contains(
                            department))
                    .ToArray();

            if (Departments.Count == 0)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(Department))
            {
                Department =
                    Departments[0];
            }
            else if (!Departments.Contains(
                         Department,
                         StringComparer.OrdinalIgnoreCase))
            {
                return Forbid();
            }
        }
        else
        {
            Departments =
                allDepartments;
        }

        DataRefreshedAtUtc =
            await _repository
                .GetFacultyRefreshTimeAsync(
                    Department,
                    cancellationToken);

        /*
        * Get total independently so pagination cannot depend
        * on the multi-result query.
        */
        TotalCount =
            await _repository
                .GetFacultyCountAsync(
                    Search,
                    Department,
                    cancellationToken);

        if (TotalPages > 0 &&
            PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var result =
            await _repository.GetFacultyPageAsync(
                Search,
                Department,
                Sort,
                Direction,
                PageNumber,
                PageSize,
                cancellationToken);

        Faculty =
            result.Items;

        return Page();
    }
}
