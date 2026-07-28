namespace OpenAlex.Worker.Jobs;

public sealed class FacultyAuthorIdentityResolutionPreviewOptions
{
    public long? SourceWorkBackfillRunId { get; set; }
    public long FacultyId { get; set; } = 16;
    public int MaxWorks { get; set; } = 5;
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B3";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B3-IdentityResolutionPreview";
}
