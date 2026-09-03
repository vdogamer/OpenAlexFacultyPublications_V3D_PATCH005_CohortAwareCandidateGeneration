using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class DepartmentPublicationsModel : PageModel
{
    private readonly AcademicOnlineReportingStoreRepository _repository;

    public DepartmentPublicationsModel(
        AcademicOnlineReportingStoreRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public long? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Top { get; set; } = 50;

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool VerifiedOnly { get; set; }

    public IReadOnlyList<AcademicOnlineDepartmentListItem> Departments { get; private set; } =
        Array.Empty<AcademicOnlineDepartmentListItem>();

    public IReadOnlyList<AcademicOnlineDepartmentPublicationRow> Publications { get; private set; } =
        Array.Empty<AcademicOnlineDepartmentPublicationRow>();

    public AcademicOnlineDepartmentListItem? SelectedDepartment { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string GetDisplayTitle(string? title)
    {
        return AcademicOnlineTextCleaner.ToPlainText(
            title,
            "(Untitled publication)");
    }

    public string CacheStatusLabel =>
        SelectedDepartment?.IsCacheComplete switch
        {
            true => "COMPLETE",
            false => "PARTIAL",
            _ => "UNKNOWN"
        };

    public string CacheScopeLabel
    {
        get
        {
            if (SelectedDepartment is null)
            {
                return string.Empty;
            }

            var fromYear =
                SelectedDepartment.SyncFromYear?.ToString() ?? "all years";
            var toYear =
                SelectedDepartment.SyncToYear?.ToString() ?? "present";
            var verification =
                SelectedDepartment.SyncVerifiedOnly == true
                    ? "verified publications only"
                    : "all returned publications";

            return $"{fromYear}–{toYear}; {verification}";
        }
    }

    public IReadOnlyList<string> CacheWarnings
    {
        get
        {
            if (SelectedDepartment is null ||
                SelectedDepartment.IsCacheComplete != false)
            {
                return Array.Empty<string>();
            }

            var warnings = new List<string>();

            if (SelectedDepartment.ScholarsDiscovered.HasValue &&
                SelectedDepartment.ScholarsProcessed.HasValue &&
                SelectedDepartment.ScholarsProcessed <
                SelectedDepartment.ScholarsDiscovered)
            {
                warnings.Add(
                    $"Only {SelectedDepartment.ScholarsProcessed} of " +
                    $"{SelectedDepartment.ScholarsDiscovered} discovered scholars " +
                    "were processed in the latest sync.");
            }

            if (SelectedDepartment.ScholarPaginationSafeguardHit)
            {
                warnings.Add(
                    "The scholar pagination safeguard was reached, so the discovered scholar count may itself be incomplete.");
            }

            if (SelectedDepartment.PublicationPaginationSafeguardHitCount > 0)
            {
                warnings.Add(
                    $"{SelectedDepartment.PublicationPaginationSafeguardHitCount} scholar publication fetch(es) reached the publication pagination safeguard.");
            }

            return warnings;
        }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Top = Math.Clamp(Top, 1, 500);

            if (FromYear.HasValue &&
                (FromYear < 1800 ||
                 FromYear > DateTime.UtcNow.Year + 1))
            {
                throw new InvalidOperationException(
                    "From year is outside the allowed range.");
            }

            Departments = await _repository.GetDepartmentsAsync(
                cancellationToken);

            if (!DepartmentId.HasValue)
            {
                return;
            }

            SelectedDepartment = Departments.FirstOrDefault(
                item => item.DepartmentId == DepartmentId.Value);

            if (SelectedDepartment is null)
            {
                throw new InvalidOperationException(
                    "The selected department is not present in the local Academic Online reporting store.");
            }

            Publications =
                await _repository.GetNewestPublicationsByDepartmentAsync(
                    DepartmentId.Value,
                    Top,
                    FromYear,
                    VerifiedOnly,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
