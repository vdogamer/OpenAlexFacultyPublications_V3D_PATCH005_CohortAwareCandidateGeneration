namespace OpenAlex.Data.Models;

public sealed class OperationalDashboardSnapshot
{
    public IReadOnlyList<OperationalMetricCard> Metrics { get; set; } = Array.Empty<OperationalMetricCard>();
    public IReadOnlyList<OperationalLogListItem> RecentLogs { get; set; } = Array.Empty<OperationalLogListItem>();
    public IReadOnlyList<RecentIngestionRunListItem> RecentRuns { get; set; } = Array.Empty<RecentIngestionRunListItem>();
    public IReadOnlyList<GenericOperationalRow> RecentErrors { get; set; } = Array.Empty<GenericOperationalRow>();
    public IReadOnlyList<GenericOperationalRow> RecentHealthChecks { get; set; } = Array.Empty<GenericOperationalRow>();
}

public sealed class OperationalMetricCard
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = "Neutral";
    public string Detail { get; set; } = string.Empty;
}

public sealed class GenericOperationalTable
{
    public string Title { get; set; } = string.Empty;
    public string SourceObject { get; set; } = string.Empty;
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<GenericOperationalRow> Rows { get; set; } = Array.Empty<GenericOperationalRow>();
}

public sealed class GenericOperationalRow
{
    public Dictionary<string, string?> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
