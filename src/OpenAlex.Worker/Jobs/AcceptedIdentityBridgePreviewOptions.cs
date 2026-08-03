namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityBridgePreviewOptions
{
    public long? SourceIdentityResolutionPreviewRunId { get; set; }
    public long FacultyId { get; set; } = 16;
    public int MaxIdentities { get; set; } = 5;
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B4";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B4-AcceptedIdentityBridgePreview";
}
