/*
V3E PATCH021H — Verified Coauthor Recovery Validation
Run after the next Neurosurgery refresh.
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
    ResolutionMethod,
    LastResolvedAtUtc,
    ResolutionEvidence,
    ResolutionError
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1
ORDER BY
    CASE WHEN AcademicOnlineAuthorId IS NULL THEN 0 ELSE 1 END,
    SearchPriority,
    DisplayName;

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

SELECT TOP (50)
    DepartmentName,
    PublicationYear,
    Title,
    FacultyNames,
    FacultyCount,
    Venue,
    Doi,
    OpenAlexId,
    Verified,
    LastRetrievedAtUtc
FROM academiconline.vw_DepartmentNewestPublication
WHERE DepartmentName = N'Department of Neurosurgery'
ORDER BY
    PublicationYear DESC,
    PublicationKey DESC;
