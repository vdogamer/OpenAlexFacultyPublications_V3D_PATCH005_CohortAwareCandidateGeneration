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
            FacultyPublicationCandidateId = row.FacultyPublicationCandidateId,
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
            ReviewedAtUtc = row.ReviewedAtUtc,
            ReviewerNotes = row.ReviewerNotes,
            MatchScore = row.MatchScore,
            EvidenceBoost = ExtractEvidenceValue(row.EvidenceSummary, "EvidenceBoost"),
            AffiliatedInstitutionMatchCount = ExtractEvidenceValue(row.EvidenceSummary, "AffiliatedInstitutionMatchCount"),
            AffiliatedInstitutionEvidenceWeight = ExtractEvidenceValue(row.EvidenceSummary, "AffiliatedInstitutionEvidenceWeight"),
            AffiliatedInstitutionNames = ExtractEvidenceValue(row.EvidenceSummary, "AffiliatedInstitutionNames"),
            KnownCoauthorOverlapCount = ExtractEvidenceValue(row.EvidenceSummary, "KnownCoauthorOverlapCount"),
            KnownCoauthorHistoricalPublicationCount = ExtractEvidenceValue(row.EvidenceSummary, "KnownCoauthorHistoricalPublicationCount"),
            KnownCoauthorNames = ExtractEvidenceValue(row.EvidenceSummary, "KnownCoauthorNames"),
            NearDuplicateResolutionStatus = row.NearDuplicateResolutionStatus,
            RelatedFacultyPublicationCandidateId = row.RelatedFacultyPublicationCandidateId,
            NearDuplicateResolutionReason = row.NearDuplicateResolutionReason,
            EvidenceSummary = row.EvidenceSummary
        });

        var csv = _csvExportService.CreateAcceptedPublicationsCsv(exportRows);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var fileName = "accepted-publications-evidence-v3e-patch015.csv";

        return File(bytes, "text/csv", fileName);
    }

    private static string? ExtractEvidenceValue(string? evidenceSummary, string key)
    {
        if (string.IsNullOrWhiteSpace(evidenceSummary) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var pattern = key + "=";
        var start = evidenceSummary.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += pattern.Length;
        var end = evidenceSummary.IndexOf(';', start);
        var value = end < 0
            ? evidenceSummary.Substring(start)
            : evidenceSummary.Substring(start, end - start);

        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) || value == "(none)" ? null : value;
    }
}
