namespace OpenAlex.Data.Models;

public sealed class OperationalLogListItem
{
    public long OperationalLogId { get; set; }
    public string SourceApplication { get; set; } = string.Empty;
    public string? EnvironmentName { get; set; }
    public string LogLevel { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? EventName { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
