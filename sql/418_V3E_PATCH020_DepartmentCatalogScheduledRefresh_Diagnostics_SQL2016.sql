/*
V3E PATCH020 — Department Catalog / Refresh Diagnostics
*/

SET NOCOUNT ON;

SELECT
    DepartmentId,
    CanonicalName AS DepartmentName,
    IsActive,
    RefreshEnabled,
    RefreshIntervalHours,
    RefreshFailureRetryMinutes,
    RefreshFromYear,
    RefreshToYear,
    RefreshVerifiedOnly,
    RefreshMaxScholars,
    CurrentSnapshotRunId,
    CurrentSnapshotAtUtc,
    NextRefreshDueAtUtc,
    DueNow =
        CASE
            WHEN RefreshEnabled = 1
             AND (NextRefreshDueAtUtc IS NULL OR NextRefreshDueAtUtc <= SYSUTCDATETIME())
             AND (RefreshLeaseUntilUtc IS NULL OR RefreshLeaseUntilUtc < SYSUTCDATETIME())
                THEN 1
            ELSE 0
        END,
    LastRefreshAttemptAtUtc,
    LastRefreshAttemptStatus,
    LastRefreshAttemptRunId,
    RefreshLeaseOwner,
    RefreshLeaseUntilUtc,
    LastRefreshError
FROM academiconline.Department
ORDER BY CanonicalName;

SELECT
    EnabledDepartments =
        SUM(CASE WHEN RefreshEnabled = 1 THEN 1 ELSE 0 END),
    DueDepartments =
        SUM
        (
            CASE
                WHEN RefreshEnabled = 1
                 AND (NextRefreshDueAtUtc IS NULL OR NextRefreshDueAtUtc <= SYSUTCDATETIME())
                 AND (RefreshLeaseUntilUtc IS NULL OR RefreshLeaseUntilUtc < SYSUTCDATETIME())
                    THEN 1
                ELSE 0
            END
        ),
    LeasedDepartments =
        SUM
        (
            CASE
                WHEN RefreshLeaseUntilUtc >= SYSUTCDATETIME()
                    THEN 1
                ELSE 0
            END
        )
FROM academiconline.Department;

SELECT TOP (20)
    AcademicOnlineSyncRunId,
    DepartmentName,
    RunStatus,
    IsCacheComplete,
    SnapshotPromoted,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CompletedAtUtc
FROM academiconline.SyncRun
ORDER BY AcademicOnlineSyncRunId DESC;
