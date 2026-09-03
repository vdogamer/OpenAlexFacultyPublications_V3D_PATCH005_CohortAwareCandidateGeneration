/*
V3E PATCH019B — Current-State Reconciliation Diagnostics
*/

SET NOCOUNT ON;

SELECT
    d.DepartmentId,
    d.CanonicalName AS DepartmentName,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc,
    SnapshotRunStatus = r.RunStatus,
    r.IsCacheComplete,
    r.SnapshotPromoted,
    r.FromYear,
    r.ToYear,
    r.VerifiedOnly,
    CurrentScholarCount =
    (
        SELECT COUNT(*)
        FROM academiconline.ScholarDepartment sd
        WHERE sd.DepartmentId = d.DepartmentId
          AND sd.IsCurrent = 1
          AND sd.CurrentSnapshotRunId = d.CurrentSnapshotRunId
    ),
    CurrentPublicationCount =
    (
        SELECT COUNT(*)
        FROM academiconline.DepartmentPublication dp
        WHERE dp.DepartmentId = d.DepartmentId
          AND dp.IsCurrent = 1
          AND dp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
    ),
    HistoricalScholarDepartmentRows =
    (
        SELECT COUNT(*)
        FROM academiconline.ScholarDepartment sd
        WHERE sd.DepartmentId = d.DepartmentId
    ),
    HistoricalDepartmentPublicationRows =
    (
        SELECT COUNT(*)
        FROM academiconline.DepartmentPublication dp
        WHERE dp.DepartmentId = d.DepartmentId
    )
FROM academiconline.Department d
LEFT JOIN academiconline.SyncRun r
    ON r.AcademicOnlineSyncRunId = d.CurrentSnapshotRunId
WHERE d.IsActive = 1
ORDER BY d.CanonicalName;

SELECT TOP (20)
    r.AcademicOnlineSyncRunId,
    r.DepartmentName,
    r.RunStatus,
    r.ScholarsDiscovered,
    r.ScholarsProcessed,
    r.IsCacheComplete,
    r.SnapshotPromoted,
    r.FromYear,
    r.ToYear,
    r.VerifiedOnly,
    r.CompletedAtUtc
FROM academiconline.SyncRun r
ORDER BY r.AcademicOnlineSyncRunId DESC;

SELECT TOP (50)
    DepartmentName,
    PublicationYear,
    Title,
    FacultyNames,
    FacultyCount,
    Venue,
    Doi,
    OpenAlexId,
    CitationCount,
    Verified,
    LastRetrievedAtUtc
FROM academiconline.vw_DepartmentNewestPublication
ORDER BY
    PublicationYear DESC,
    PublicationKey DESC;
