namespace OpenAlex.Data.Models;

public sealed class AcademicOnlineDepartmentRefreshCatalogItem
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool RefreshEnabled { get; init; }
    public int RefreshIntervalHours { get; init; }
    public int RefreshFailureRetryMinutes { get; init; }
    public int? RefreshFromYear { get; init; }
    public int? RefreshToYear { get; init; }
    public bool RefreshVerifiedOnly { get; init; }
    public int RefreshMaxScholars { get; init; }
    public long? CurrentSnapshotRunId { get; init; }
    public DateTime? CurrentSnapshotAtUtc { get; init; }
    public DateTime? NextRefreshDueAtUtc { get; init; }
    public DateTime? LastRefreshAttemptAtUtc { get; init; }
    public string? LastRefreshAttemptStatus { get; init; }
    public long? LastRefreshAttemptRunId { get; init; }
    public string? LastRefreshError { get; init; }
    public string? RefreshLeaseOwner { get; init; }
    public DateTime? RefreshLeaseUntilUtc { get; init; }

    public bool DueNow =>
        IsActive &&
        RefreshEnabled &&
        (!NextRefreshDueAtUtc.HasValue ||
         NextRefreshDueAtUtc <= DateTime.UtcNow) &&
        (!RefreshLeaseUntilUtc.HasValue ||
         RefreshLeaseUntilUtc < DateTime.UtcNow);
}

public sealed class AcademicOnlineDepartmentRefreshPolicyUpdate
{
    public long DepartmentId { get; init; }
    public bool RefreshEnabled { get; init; }
    public int RefreshIntervalHours { get; init; }
    public int RefreshFailureRetryMinutes { get; init; }
    public int? RefreshFromYear { get; init; }
    public int? RefreshToYear { get; init; }
    public bool RefreshVerifiedOnly { get; init; }
    public int RefreshMaxScholars { get; init; }
}

public sealed class AcademicOnlineDepartmentRefreshClaim
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int RefreshIntervalHours { get; init; }
    public int RefreshFailureRetryMinutes { get; init; }
    public int? RefreshFromYear { get; init; }
    public int? RefreshToYear { get; init; }
    public bool RefreshVerifiedOnly { get; init; }
    public int RefreshMaxScholars { get; init; }
    public string LeaseOwner { get; init; } = string.Empty;
    public DateTime LeaseUntilUtc { get; init; }
}


public sealed class AcademicOnlineDepartmentRefreshSchedulerState
{
    public string SchedulerKey { get; init; } = string.Empty;
    public DateTime LastHeartbeatAtUtc { get; init; }
    public DateTime? LastPassStartedAtUtc { get; init; }
    public DateTime? LastPassCompletedAtUtc { get; init; }
    public string? LastPassStatus { get; init; }
    public string? LeaseOwner { get; init; }
    public int? PollIntervalSeconds { get; init; }
    public int DepartmentsClaimed { get; init; }
    public int DepartmentsCompleted { get; init; }
    public int DepartmentsPartial { get; init; }
    public int DepartmentsFailed { get; init; }
    public string? LastError { get; init; }

    public bool IsHeartbeatFresh(DateTime utcNow)
    {
        if (!PollIntervalSeconds.HasValue)
        {
            return false;
        }

        var healthyWindowSeconds =
            Math.Max(60, (PollIntervalSeconds.Value * 2) + 60);

        return LastHeartbeatAtUtc >=
            utcNow.AddSeconds(-healthyWindowSeconds);
    }
}
