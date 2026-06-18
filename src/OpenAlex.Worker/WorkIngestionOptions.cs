namespace OpenAlex.Worker;

public sealed class WorkIngestionOptions
{
    public string RunMode { get; set; } = "TestLoad";
    public int MaxPages { get; set; } = 1;
    public int MaxRecords { get; set; } = 100;
    public string StartedBy { get; set; } = "OpenAlex.V2E.Worker";
    public string ApplicationVersion { get; set; } = "2E";
}
