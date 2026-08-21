namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCandidateExecutionOptions
{
    public long? PreviewRunId { get; set; }
    public long FacultyId { get; set; } = 16;
    public bool ConfirmExecution { get; set; }
    public int MaxCandidates { get; set; } = 5;
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B7";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B7-PubCandidateExec";
    public string IngestionApplicationVersion { get; set; } = "3E-PATCH017B7-PubCandidateExec";
}
