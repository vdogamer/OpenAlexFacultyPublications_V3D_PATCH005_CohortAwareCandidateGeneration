namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineWorkBackfillOptions
{
    public long? PreviewRunId { get; set; }
    public bool ConfirmInsert { get; set; }
    public int MaxWorks { get; set; } = 5;
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B1";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B1-ControlledAcademicOnlineGuidedWorkBackfill";
}
