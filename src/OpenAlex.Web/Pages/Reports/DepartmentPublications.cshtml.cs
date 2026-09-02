using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

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
