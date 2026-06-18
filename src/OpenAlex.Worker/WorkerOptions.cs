namespace OpenAlex.Worker;

public sealed class WorkerOptions
{
    public bool Enabled { get; set; } = false;
    public string JobName { get; set; } = "InstitutionIngestionPreview";
    public bool RunOnceAndStop { get; set; } = true;
    public int DelaySeconds { get; set; } = 10;
}
