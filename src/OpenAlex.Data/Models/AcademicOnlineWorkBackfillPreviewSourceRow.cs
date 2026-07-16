namespace OpenAlex.Data.Models;

public sealed class AcademicOnlineWorkBackfillPreviewSourceRow
{
    public long AcademicOnlineComparisonRunId { get; set; }
    public long AcademicOnlinePublicationSnapshotId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? AcademicOnlineScholarName { get; set; }
    public string? AcademicOnlinePublicationId { get; set; }
    public string? PublicationTitle { get; set; }
    public int? PublicationYear { get; set; }
    public string? Doi { get; set; }
    public string? OpenAlexId { get; set; }
    public string? NormalizedOpenAlexId { get; set; }
    public int SnapshotDuplicateCount { get; set; }
    public long? LocalWorkId { get; set; }
}
