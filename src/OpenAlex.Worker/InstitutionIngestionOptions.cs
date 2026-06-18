namespace OpenAlex.Worker;

public sealed class InstitutionIngestionOptions
{
    public string RunMode { get; set; } = "TestLoad";
    public int MaxPages { get; set; } = 1;
    public int MaxRecords { get; set; } = 10;
    public string StartedBy { get; set; } = "OpenAlex.V2C.Worker";
    public string ApplicationVersion { get; set; } = "2C";
}
