namespace OpenAlex.Worker;

public sealed class ExecutionWindowOptions
{
    public bool EnforceExecutionWindows { get; set; } = false;
    public bool AllowWhenWindowTableMissing { get; set; } = true;
}
