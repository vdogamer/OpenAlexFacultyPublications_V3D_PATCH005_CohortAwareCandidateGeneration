namespace OpenAlex.Data.Models;

public sealed class IntegratedJobDefinition
{
    public string JobName { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string RecommendedRunMode { get; set; } = "TestLoad";
    public bool WritesData { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RecentIngestionRunListItem
{
    public long IngestionRunId { get; set; }
    public string? RunMode { get; set; }
    public string? Status { get; set; }
    public string? InstitutionFilter { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public long? ElapsedMs { get; set; }
    public string? StartedBy { get; set; }
    public string? MachineName { get; set; }
    public string? ApplicationVersion { get; set; }
    public int? ErrorCount { get; set; }
    public int? WarningCount { get; set; }
}

public sealed class JobDashboardSnapshot
{
    public IReadOnlyList<IntegratedJobDefinition> Jobs { get; set; } = Array.Empty<IntegratedJobDefinition>();
    public IReadOnlyList<RecentIngestionRunListItem> RecentRuns { get; set; } = Array.Empty<RecentIngestionRunListItem>();
}
