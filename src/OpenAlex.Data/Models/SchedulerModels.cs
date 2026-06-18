namespace OpenAlex.Data.Models;

public sealed class SchedulerJobDefinition
{
    public long JobDefinitionId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string JobDisplayName { get; set; } = string.Empty;
    public string JobCategory { get; set; } = string.Empty;
    public string? JobDescription { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsWriteJob { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string DefaultRunMode { get; set; } = "TestLoad";
    public int? DefaultMaxPages { get; set; }
    public int? DefaultMaxRecords { get; set; }
    public int SortOrder { get; set; }
}

public sealed class JobExecutionRequestListItem
{
    public long JobExecutionRequestId { get; set; }
    public long JobDefinitionId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string RequestStatus { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public string? RequestReason { get; set; }
    public string RunMode { get; set; } = "TestLoad";
    public int? MaxPages { get; set; }
    public int? MaxRecords { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? WorkerMachineName { get; set; }
    public string? WorkerApplicationVersion { get; set; }
    public long? IngestionRunId { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class JobExecutionEventListItem
{
    public long JobExecutionEventId { get; set; }
    public long JobExecutionRequestId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventLevel { get; set; } = string.Empty;
    public string? EventMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class SchedulerDashboardSnapshot
{
    public IReadOnlyList<SchedulerJobDefinition> Jobs { get; set; } = Array.Empty<SchedulerJobDefinition>();
    public IReadOnlyList<JobExecutionRequestListItem> RecentRequests { get; set; } = Array.Empty<JobExecutionRequestListItem>();
}

public sealed class QueueJobRequestModel
{
    public long JobDefinitionId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string? RequestReason { get; set; }
    public string ConfirmationText { get; set; } = string.Empty;
    public string RunMode { get; set; } = "TestLoad";
    public int? MaxPages { get; set; }
    public int? MaxRecords { get; set; }
}
