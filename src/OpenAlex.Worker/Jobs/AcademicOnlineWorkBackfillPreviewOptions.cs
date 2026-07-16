namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineWorkBackfillPreviewOptions
{
    public long? AcademicOnlineComparisonRunId { get; set; }
    public long? FacultyId { get; set; }
    public int MaxWorks { get; set; } = 5;
    public int MaxSourceRows { get; set; } = 500;
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B-AcademicOnlineGuidedWorkBackfillPreview";
}
