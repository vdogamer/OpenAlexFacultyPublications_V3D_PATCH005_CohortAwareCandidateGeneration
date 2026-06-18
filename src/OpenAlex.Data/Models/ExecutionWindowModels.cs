namespace OpenAlex.Data.Models;

public sealed class JobExecutionWindow
{
    public long JobExecutionWindowId { get; set; }
    public string WindowName { get; set; } = string.Empty;
    public string? WindowDescription { get; set; }
    public string? AppliesToJobName { get; set; }
    public bool IsEnabled { get; set; }
    public string DaysOfWeekCsv { get; set; } = string.Empty;
    public TimeSpan StartTimeLocal { get; set; }
    public TimeSpan EndTimeLocal { get; set; }
    public string TimeZoneId { get; set; } = "Eastern Standard Time";
    public bool AllowFullLoad { get; set; }
    public int MaxRequestsPerWindow { get; set; }
}
