namespace OpenAlex.Worker;

public sealed class QueuedJobProcessorOptions
{
    public int MaxRequestsPerRun { get; set; } = 1;
    public bool StopOnFirstFailure { get; set; } = true;
    public string WorkerApplicationVersion { get; set; } = "3B";
}
