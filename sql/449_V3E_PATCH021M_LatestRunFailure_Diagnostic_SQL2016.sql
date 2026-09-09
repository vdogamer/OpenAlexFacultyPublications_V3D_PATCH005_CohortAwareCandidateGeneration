/*
V3E PATCH021M — Latest Neurosurgery Run Failure Diagnostic
Read-only. SQL Server 2016 compatible.

Purpose:
- Capture the exact exception for the latest failed Neurosurgery runs.
- Split long ErrorText into readable chunks.
- Correlate operational-log entries around each run.
- Confirm whether any department/roster publication rows were touched.
*/

SET NOCOUNT ON;

DECLARE @Runs TABLE
(
    RunId BIGINT PRIMARY KEY
);

INSERT INTO @Runs (RunId)
SELECT TOP (2)
    AcademicOnlineSyncRunId
FROM academiconline.SyncRun
WHERE DepartmentName = N'Department of Neurosurgery'
  AND RunStatus = N'Failed'
ORDER BY AcademicOnlineSyncRunId DESC;

SELECT
    r.AcademicOnlineSyncRunId,
    r.DepartmentName,
    r.RunStatus,
    r.ScholarsDiscovered,
    r.ScholarsProcessed,
    r.PublicationsProcessed,
    r.ScholarPagesFetched,
    r.PublicationPagesFetched,
    r.IsCacheComplete,
    r.StartedAtUtc,
    r.CompletedAtUtc,
    r.SummaryText,
    r.ErrorText
FROM academiconline.SyncRun r
JOIN @Runs x
    ON x.RunId = r.AcademicOnlineSyncRunId
ORDER BY r.AcademicOnlineSyncRunId DESC;

;WITH ErrorRows AS
(
    SELECT
        r.AcademicOnlineSyncRunId,
        ErrorText =
            COALESCE(
                r.ErrorText,
                N'(no ErrorText)')
    FROM academiconline.SyncRun r
    JOIN @Runs x
        ON x.RunId = r.AcademicOnlineSyncRunId
),
Parts AS
(
    SELECT 1 AS PartNumber, 1 AS StartPosition
    UNION ALL SELECT 2, 1001
    UNION ALL SELECT 3, 2001
    UNION ALL SELECT 4, 3001
    UNION ALL SELECT 5, 4001
    UNION ALL SELECT 6, 5001
    UNION ALL SELECT 7, 6001
    UNION ALL SELECT 8, 7001
)
SELECT
    e.AcademicOnlineSyncRunId,
    ErrorTextLength = LEN(e.ErrorText),
    p.PartNumber,
    ErrorPart =
        SUBSTRING(
            e.ErrorText,
            p.StartPosition,
            1000)
FROM ErrorRows e
CROSS JOIN Parts p
WHERE p.StartPosition <= LEN(e.ErrorText)
ORDER BY
    e.AcademicOnlineSyncRunId DESC,
    p.PartNumber;

SELECT TOP (100)
    l.CreatedAtUtc,
    l.LogLevel,
    l.Category,
    l.EventName,
    l.Message,
    l.ExceptionType,
    l.ExceptionMessage
FROM openalex.OperationalLog l
WHERE EXISTS
(
    SELECT 1
    FROM academiconline.SyncRun r
    JOIN @Runs x
        ON x.RunId = r.AcademicOnlineSyncRunId
    WHERE l.CreatedAtUtc >= DATEADD(MINUTE, -2, r.StartedAtUtc)
      AND l.CreatedAtUtc <= DATEADD(MINUTE,  2, r.CompletedAtUtc)
)
ORDER BY l.CreatedAtUtc DESC;

SELECT
    dp.LastSyncRunId,
    DepartmentRowsTouched = COUNT(*)
FROM academiconline.DepartmentPublication dp
WHERE dp.DepartmentId =
(
    SELECT TOP (1)
        DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName = N'Department of Neurosurgery'
      AND IsActive = 1
)
AND dp.LastSyncRunId IN
(
    SELECT RunId
    FROM @Runs
)
GROUP BY dp.LastSyncRunId
ORDER BY dp.LastSyncRunId DESC;

SELECT
    drp.LastSyncRunId,
    drp.SourceProvider,
    RosterMembersRepresented =
        COUNT(DISTINCT drp.DepartmentRosterMemberId),
    AttributionRows =
        COUNT(*),
    UniquePublications =
        COUNT(DISTINCT drp.PublicationKey)
FROM academiconline.DepartmentRosterPublication drp
WHERE drp.DepartmentId =
(
    SELECT TOP (1)
        DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName = N'Department of Neurosurgery'
      AND IsActive = 1
)
AND drp.LastSyncRunId IN
(
    SELECT RunId
    FROM @Runs
)
GROUP BY
    drp.LastSyncRunId,
    drp.SourceProvider
ORDER BY
    drp.LastSyncRunId DESC,
    drp.SourceProvider;
