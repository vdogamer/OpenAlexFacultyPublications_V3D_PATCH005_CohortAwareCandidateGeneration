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
    private readonly AcademicOnlineDepartmentDiscoveryRepository _discoveryRepository;

    public AcademicOnlineDepartmentsModel(
        AcademicOnlineDepartmentRefreshRepository repository,
        AcademicOnlineDepartmentDiscoveryRepository discoveryRepository)
    {
        _repository = repository;
        _discoveryRepository = discoveryRepository;
    }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    public IReadOnlyList<AcademicOnlineDepartmentRefreshCatalogItem>
        Departments { get; private set; } =
            Array.Empty<AcademicOnlineDepartmentRefreshCatalogItem>();

    public AcademicOnlineDepartmentRefreshSchedulerState?
        SchedulerState { get; private set; }

    public AcademicOnlineDepartmentDiscoveryStatus?
        DiscoveryStatus { get; private set; }

    public string SchedulerHealthLabel
    {
        get
        {
            if (SchedulerState is null)
            {
                return "NOT STARTED";
            }

            if (SchedulerState.IsContinuous != true)
            {
                return "ONE-PASS";
            }

            return SchedulerState.IsHeartbeatFresh(DateTime.UtcNow)
                ? "ACTIVE"
                : "STALE";
        }
    }

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        Departments = await _repository.GetCatalogAsync(
            cancellationToken);

        SchedulerState =
            await _repository.GetSchedulerStateAsync(
                "AcademicOnlineDepartmentRefreshScheduler",
                cancellationToken);

        DiscoveryStatus =
            await _discoveryRepository.GetLatestStatusAsync(
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

    public async Task<IActionResult> OnPostQueueDiscoveryAsync(
        string parentName,
        int maxPages,
        CancellationToken cancellationToken)
    {
        var normalizedParentName =
            string.IsNullOrWhiteSpace(parentName)
                ? "School of Medicine"
                : parentName.Trim();

        var requestId =
            await _discoveryRepository.QueueRequestAsync(
                normalizedParentName,
                rowsPerPage: 100,
                maxPages: Math.Clamp(maxPages, 1, 1000),
                requestedBy:
                    User.Identity?.Name
                    ?? "OpenAlex.Admin",
                cancellationToken);

        return RedirectToPage(
            new
            {
                message =
                    $"Department discovery request {requestId} is queued for the background scheduler."
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
