/*
V3E PATCH021G — Faculty-Roster Identity Diagnostics
Run after the next Department of Neurosurgery refresh attempt.
*/

SET NOCOUNT ON;

DECLARE @DepartmentId BIGINT =
(
    SELECT TOP (1) DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName = N'Department of Neurosurgery'
      AND IsActive = 1
    ORDER BY DepartmentId
);

SELECT TOP (5)
    AcademicOnlineSyncRunId,
    DepartmentName,
    RunStatus,
    ScholarsDiscovered,
    ScholarsProcessed,
    IsCacheComplete,
    PublicationsProcessed,
    ScholarPagesFetched,
    PublicationPagesFetched,
    StartedAtUtc,
    CompletedAtUtc,
    SummaryText,
    ErrorText
FROM academiconline.SyncRun
WHERE DepartmentName = N'Department of Neurosurgery'
ORDER BY AcademicOnlineSyncRunId DESC;

SELECT
    TotalRosterMembers = COUNT(*),
    ResolvedRosterMembers =
        SUM(CASE WHEN AcademicOnlineAuthorId IS NOT NULL THEN 1 ELSE 0 END),
    UnresolvedRosterMembers =
        SUM(CASE WHEN AcademicOnlineAuthorId IS NULL THEN 1 ELSE 0 END)
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1;

SELECT
    DisplayName,
    ExpectedEmail,
    AcademicOnlineAuthorId,
    ResolutionStatus,
    LastResolvedAtUtc,
    ResolutionError
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1
ORDER BY
    CASE WHEN AcademicOnlineAuthorId IS NULL THEN 0 ELSE 1 END,
    SearchPriority,
    DisplayName;

SELECT
    DisplayName,
    ResolutionErrorLength = LEN(ResolutionError),
    ErrorPart1 = SUBSTRING(ResolutionError, 1, 1000),
    ErrorPart2 = SUBSTRING(ResolutionError, 1001, 1000),
    ErrorPart3 = SUBSTRING(ResolutionError, 2001, 1000),
    ErrorPart4 = SUBSTRING(ResolutionError, 3001, 1000)
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1
  AND AcademicOnlineAuthorId IS NULL
ORDER BY SearchPriority, DisplayName;

SELECT
    DepartmentId,
    CanonicalName,
    RefreshEnabled,
    LastRefreshAttemptStatus,
    LastRefreshAttemptRunId,
    LastRefreshAttemptAtUtc,
    LastRefreshError,
    CurrentSnapshotRunId,
    CurrentSnapshotAtUtc
FROM academiconline.Department
WHERE DepartmentId = @DepartmentId;
