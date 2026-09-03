using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class AcademicOnlineDepartmentsModel : PageModel
{
    private readonly AcademicOnlineDepartmentRefreshRepository _repository;

    public AcademicOnlineDepartmentsModel(
        AcademicOnlineDepartmentRefreshRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    public IReadOnlyList<AcademicOnlineDepartmentRefreshCatalogItem>
        Departments { get; private set; } =
            Array.Empty<AcademicOnlineDepartmentRefreshCatalogItem>();

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        Departments = await _repository.GetCatalogAsync(
            cancellationToken);
    }

    public async Task<IActionResult> OnPostSavePolicyAsync(
        long departmentId,
        bool refreshEnabled,
        int refreshIntervalHours,
        int refreshFailureRetryMinutes,
        int? refreshFromYear,
        int? refreshToYear,
        bool refreshVerifiedOnly,
        int refreshMaxScholars,
        CancellationToken cancellationToken)
    {
        await _repository.UpdatePolicyAsync(
            new AcademicOnlineDepartmentRefreshPolicyUpdate
            {
                DepartmentId = departmentId,
                RefreshEnabled = refreshEnabled,
                RefreshIntervalHours = refreshIntervalHours,
                RefreshFailureRetryMinutes =
                    refreshFailureRetryMinutes,
                RefreshFromYear = refreshFromYear,
                RefreshToYear = refreshToYear,
                RefreshVerifiedOnly = refreshVerifiedOnly,
                RefreshMaxScholars = refreshMaxScholars
            },
            cancellationToken);

        return RedirectToPage(
            new
            {
                message =
                    "Department refresh policy saved."
            });
    }

    public async Task<IActionResult> OnPostDueNowAsync(
        long departmentId,
        CancellationToken cancellationToken)
    {
        await _repository.SetDueNowAsync(
            departmentId,
            cancellationToken);

        return RedirectToPage(
            new
            {
                message =
                    "Department marked due for the next scheduler pass."
            });
    }
}
