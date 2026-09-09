/*
V3E PATCH021L — Hardening Validation
Read-only. SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @DepartmentId BIGINT =
(
    SELECT TOP (1)
        DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName =
        N'Department of Neurosurgery'
      AND IsActive = 1
    ORDER BY DepartmentId
);

SELECT
    PublicationRows =
        COUNT(*),
    NullPublicationYears =
        SUM(
            CASE
                WHEN PublicationYear IS NULL
                    THEN 1
                ELSE 0
            END),
    InvalidPublicationYears =
        SUM(
            CASE
                WHEN PublicationYear IS NOT NULL
                 AND
                 (
                     PublicationYear < 1000
                     OR PublicationYear > 2100
                 )
                    THEN 1
                ELSE 0
            END),
    NegativeCitationCounts =
        SUM(
            CASE
                WHEN CitationCount < 0
                    THEN 1
                ELSE 0
            END)
FROM academiconline.Publication;

SELECT
    ConstraintName =
        name,
    IsDisabled =
        is_disabled,
    IsNotTrusted =
        is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id =
      OBJECT_ID(N'academiconline.Publication')
  AND name IN
  (
      N'CK_academiconline_Publication_PublicationYear',
      N'CK_academiconline_Publication_CitationCount'
  )
ORDER BY name;

SELECT
    d.DepartmentId,
    d.CanonicalName,
    d.SourceMode,
    d.LastRefreshAttemptStatus,
    d.LastRefreshAttemptRunId,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc,
    LatestCompletedRunId =
    (
        SELECT MAX(r.AcademicOnlineSyncRunId)
        FROM academiconline.SyncRun r
        WHERE r.DepartmentName =
              d.CanonicalName
          AND r.RunStatus =
              N'Completed'
    )
FROM academiconline.Department d
WHERE d.DepartmentId =
      @DepartmentId;

SELECT TOP (5)
    AcademicOnlineSyncRunId,
    DepartmentName,
    RunStatus,
    ScholarsDiscovered,
    ScholarsProcessed,
    IsCacheComplete,
    PublicationsProcessed,
    StartedAtUtc,
    CompletedAtUtc,
    ErrorText
FROM academiconline.SyncRun
WHERE DepartmentName =
      N'Department of Neurosurgery'
ORDER BY
    AcademicOnlineSyncRunId DESC;

SELECT
    CurrentReportRows =
        COUNT(*),
    CurrentReportNullYears =
        SUM(
            CASE
                WHEN PublicationYear IS NULL
                    THEN 1
                ELSE 0
            END),
    CurrentReportInvalidYears =
        SUM(
            CASE
                WHEN PublicationYear IS NOT NULL
                 AND
                 (
                     PublicationYear < 1000
                     OR PublicationYear > 2100
                 )
                    THEN 1
                ELSE 0
            END)
FROM academiconline.vw_DepartmentNewestPublication
WHERE DepartmentName =
      N'Department of Neurosurgery';

SELECT
    SourceProvider,
    CurrentRosterMembersRepresented =
        COUNT(DISTINCT DepartmentRosterMemberId),
    CurrentAttributionRows =
        COUNT(*),
    CurrentUniquePublications =
        COUNT(DISTINCT PublicationKey)
FROM academiconline.DepartmentRosterPublication
WHERE DepartmentId =
      @DepartmentId
  AND IsCurrent = 1
  AND CurrentSnapshotRunId =
      (
          SELECT CurrentSnapshotRunId
          FROM academiconline.Department
          WHERE DepartmentId =
                @DepartmentId
      )
GROUP BY
    SourceProvider
ORDER BY
    SourceProvider;

SELECT TOP (50)
    PublicationKey,
    AcademicOnlinePublicationId,
    OpenAlexId,
    Doi,
    YearText,
    PublicationYear,
    CitationCount,
    Title
FROM academiconline.Publication
WHERE PublicationYear IS NULL
ORDER BY
    LastRetrievedAtUtc DESC,
    PublicationKey DESC;
