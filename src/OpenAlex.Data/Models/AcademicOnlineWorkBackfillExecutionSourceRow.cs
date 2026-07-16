namespace OpenAlex.Data.Models;

public sealed class AcademicOnlineWorkBackfillExecutionSourceRow
{
    public long AcademicOnlineWorkBackfillPreviewItemId { get; set; }
    public long AcademicOnlineWorkBackfillPreviewRunId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? AcademicOnlinePublicationId { get; set; }
    public string? AcademicOnlinePublicationTitle { get; set; }
    public int? AcademicOnlinePublicationYear { get; set; }
    public string? NormalizedOpenAlexId { get; set; }
    public string? ReturnedOpenAlexId { get; set; }
    public string? ReturnedTitle { get; set; }
    public string? RawResponseJson { get; set; }
}
