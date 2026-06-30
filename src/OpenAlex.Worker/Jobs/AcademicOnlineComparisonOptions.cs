namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineComparisonOptions
{
    public string RunMode { get; set; } = "TestLoad";
    public string? PilotCohortName { get; set; }
    public int MaxFacultyToProcess { get; set; } = 1;
    public int RowsPerPage { get; set; } = 25;
    public int? FromYear { get; set; }
    public int? ToYear { get; set; }
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH016";
    public string ApplicationVersion { get; set; } = "3E-PATCH016-AcademicOnlineSourceComparison";
}
