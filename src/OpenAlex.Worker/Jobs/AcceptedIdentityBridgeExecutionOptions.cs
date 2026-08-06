namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityBridgeExecutionOptions
{
    public long? PreviewRunId { get; set; }
    public long FacultyId { get; set; } = 16;
    public int MaxIdentities { get; set; } = 3;
    public bool ConfirmBridge { get; set; }
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B5";
    public string ReviewedBy { get; set; } = "OpenAlex.V3E.PATCH017B5";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B5-AcceptedIdentityBridgeExecution";
    public string IngestionApplicationVersion { get; set; } = "3E-PATCH017B5-IdentityBridge";
}
