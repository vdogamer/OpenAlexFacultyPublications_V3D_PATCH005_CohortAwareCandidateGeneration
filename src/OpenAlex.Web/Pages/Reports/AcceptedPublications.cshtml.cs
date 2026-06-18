using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Reporting;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class AcceptedPublicationsModel : PageModel
{
    private readonly ReportingRepository _repository;
    private readonly CsvExportService _csvExportService;

    public AcceptedPublicationsModel(
        ReportingRepository repository,
        CsvExportService csvExportService)
    {
        _repository = repository;
        _csvExportService = csvExportService;
    }

    public IReadOnlyList<AcceptedPublicationDataRow> Rows { get; private set; } = Array.Empty<AcceptedPublicationDataRow>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await _repository.GetAcceptedPublicationsAsync(500, cancellationToken);
    }

    public async Task<IActionResult> OnGetCsvAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetAcceptedPublicationsAsync(5000, cancellationToken);

        var exportRows = rows.Select(row => new AcceptedPublicationExportRow
        {
            FacultyDisplayName = row.FacultyDisplayName,
            PrimaryEmail = row.PrimaryEmail,
            DepartmentName = row.DepartmentName,
            DivisionName = row.DivisionName,
            AppointmentTitle = row.AppointmentTitle,
            AuthorDisplayName = row.AuthorDisplayName,
            AuthorOpenAlexId = row.AuthorOpenAlexId,
            AuthorOrcid = row.AuthorOrcid,
            WorkTitle = row.WorkTitle,
            PublicationYear = row.PublicationYear,
            PublicationDate = row.PublicationDate,
            WorkDoi = row.WorkDoi,
            DoiUrl = row.DoiUrl,
            WorkOpenAlexId = row.WorkOpenAlexId,
            WorkType = row.WorkType,
            CitedByCount = row.CitedByCount,
            DecisionStatus = row.DecisionStatus,
            ReviewedBy = row.ReviewedBy,
            ReviewedAtUtc = row.ReviewedAtUtc
        });

        var csv = _csvExportService.CreateAcceptedPublicationsCsv(exportRows);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var fileName = "accepted-publications-v2b.csv";

        return File(bytes, "text/csv", fileName);
    }
}
