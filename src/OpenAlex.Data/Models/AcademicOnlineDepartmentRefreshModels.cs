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
