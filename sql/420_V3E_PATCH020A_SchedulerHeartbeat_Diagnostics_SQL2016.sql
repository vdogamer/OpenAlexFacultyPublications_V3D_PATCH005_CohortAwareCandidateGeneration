/*
V3E PATCH020A — Continuous Scheduler Heartbeat Diagnostics
*/

SET NOCOUNT ON;

SELECT
    SchedulerKey,
    LastHeartbeatAtUtc,
    LastPassStartedAtUtc,
    LastPassCompletedAtUtc,
    LastPassStatus,
    LeaseOwner,
    PollIntervalSeconds,
    IsContinuous,
    DepartmentsClaimed,
    DepartmentsCompleted,
    DepartmentsPartial,
    DepartmentsFailed,
    LastError,
    SecondsSinceHeartbeat =
        DATEDIFF(SECOND, LastHeartbeatAtUtc, SYSUTCDATETIME()),
    ExpectedHealthyWindowSeconds =
        CASE
            WHEN PollIntervalSeconds IS NULL THEN NULL
            ELSE (PollIntervalSeconds * 2) + 60
        END,
    HeartbeatHealth =
        CASE
            WHEN IsContinuous = 0 THEN N'ONE-PASS'
            WHEN IsContinuous IS NULL OR PollIntervalSeconds IS NULL THEN N'UNKNOWN'
            WHEN DATEDIFF(SECOND, LastHeartbeatAtUtc, SYSUTCDATETIME())
                 <= (PollIntervalSeconds * 2) + 60
                THEN N'ACTIVE'
            ELSE N'STALE'
        END
FROM academiconline.RefreshSchedulerState
ORDER BY SchedulerKey;

SELECT
    DepartmentId,
    CanonicalName AS DepartmentName,
    RefreshEnabled,
    NextRefreshDueAtUtc,
    LastRefreshAttemptStatus,
    LastRefreshAttemptRunId,
    RefreshLeaseOwner,
    RefreshLeaseUntilUtc,
    DueNow =
        CASE
            WHEN RefreshEnabled = 1
             AND (NextRefreshDueAtUtc IS NULL OR NextRefreshDueAtUtc <= SYSUTCDATETIME())
             AND (RefreshLeaseUntilUtc IS NULL OR RefreshLeaseUntilUtc < SYSUTCDATETIME())
                THEN 1
            ELSE 0
        END
FROM academiconline.Department
WHERE IsActive = 1
ORDER BY CanonicalName;
