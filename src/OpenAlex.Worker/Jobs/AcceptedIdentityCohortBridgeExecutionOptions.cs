namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityCohortBridgeExecutionOptions
{
    public string CohortName { get; set; } = "V3E_PATCH017B8_FIVE_FACULTY";
    public int ExpectedFacultyCount { get; set; } = 5;
    public int MaxIdentities { get; set; } = 25;
    public bool ConfirmBridge { get; set; }
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B9";
    public string ReviewedBy { get; set; } = "OpenAlex.V3E.PATCH017B9";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B9-5FacultyIdentityBridge";
}
