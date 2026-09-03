namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineDepartmentRefreshSchedulerOptions
{
    public string SchedulerKey { get; set; } =
        "AcademicOnlineDepartmentRefreshScheduler";

    public int MaxDepartmentsPerPass { get; set; } = 1;
    public int LeaseMinutes { get; set; } = 180;
    public string StartedBy { get; set; } =
        "OpenAlex.V3E.PATCH020.Scheduler";
    public string ApplicationVersion { get; set; } =
        "3E-PATCH020-DepartmentCatalogScheduledRefresh";
}
