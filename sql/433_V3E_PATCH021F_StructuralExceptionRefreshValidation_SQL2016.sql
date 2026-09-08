/*
V3E PATCH021F — Structural Exception Refresh Validation
Run after one or both exception departments have been refreshed.
*/

SET NOCOUNT ON;

SELECT
    SupportedDepartmentCount = COUNT(*),
    DirectDepartmentCount =
        SUM(CASE WHEN SourceMode = N'DirectDepartment' THEN 1 ELSE 0 END),
    AggregateUnitsCount =
        SUM(CASE WHEN SourceMode = N'AggregateUnits' THEN 1 ELSE 0 END),
    FacultyRosterCount =
        SUM(CASE WHEN SourceMode = N'FacultyRoster' THEN 1 ELSE 0 END)
FROM academiconline.Department
WHERE IsActive = 1;

SELECT
    d.DepartmentId,
    d.CanonicalName AS DepartmentName,
    d.SourceMode,
    d.RefreshEnabled,
    d.LastRefreshAttemptStatus,
    d.LastRefreshAttemptRunId,
    d.LastRefreshError,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc,
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
        )
FROM academiconline.Department d
WHERE d.CanonicalName IN
(
    N'Department of Medicine',
    N'Department of Neurosurgery'
)
ORDER BY d.CanonicalName;

SELECT
    d.CanonicalName AS DepartmentName,
    u.OfficialUnitName,
    SuccessfulAlias =
        MAX
        (
            CASE
                WHEN u.LastAttemptStatus IN (N'Resolved', N'Partial')
                    THEN u.AcademicOnlineSearchName
                ELSE NULL
            END
        ),
    SuccessfulScholarCount =
        MAX
        (
            CASE
                WHEN u.LastAttemptStatus IN (N'Resolved', N'Partial')
                    THEN u.LastScholarCount
                ELSE NULL
            END
        ),
    ResolutionState =
        CASE
            WHEN MAX(CASE WHEN u.LastAttemptStatus IN (N'Resolved', N'Partial') THEN 1 ELSE 0 END) = 1
                THEN N'Resolved'
            ELSE N'Unresolved'
        END
FROM academiconline.DepartmentSourceUnit u
JOIN academiconline.Department d
    ON d.DepartmentId = u.DepartmentId
WHERE d.SourceMode = N'AggregateUnits'
  AND u.IsActive = 1
GROUP BY
    d.CanonicalName,
    u.OfficialUnitName
ORDER BY
    d.CanonicalName,
    u.OfficialUnitName;

SELECT
    d.CanonicalName AS DepartmentName,
    TotalRosterMembers = COUNT(*),
    ResolvedRosterMembers =
        SUM(CASE WHEN r.AcademicOnlineAuthorId IS NOT NULL THEN 1 ELSE 0 END),
    UnresolvedRosterMembers =
        SUM(CASE WHEN r.AcademicOnlineAuthorId IS NULL THEN 1 ELSE 0 END)
FROM academiconline.DepartmentRosterMember r
JOIN academiconline.Department d
    ON d.DepartmentId = r.DepartmentId
WHERE d.SourceMode = N'FacultyRoster'
  AND r.IsActive = 1
GROUP BY d.CanonicalName
ORDER BY d.CanonicalName;

SELECT
    d.CanonicalName AS DepartmentName,
    r.DisplayName,
    r.ExpectedEmail,
    r.AcademicOnlineAuthorId,
    r.ResolutionStatus,
    r.LastResolvedAtUtc,
    r.ResolutionError
FROM academiconline.DepartmentRosterMember r
JOIN academiconline.Department d
    ON d.DepartmentId = r.DepartmentId
WHERE d.SourceMode = N'FacultyRoster'
  AND r.IsActive = 1
ORDER BY
    d.CanonicalName,
    r.SearchPriority,
    r.DisplayName;

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
WHERE DepartmentName IN
(
    N'Department of Medicine',
    N'Department of Neurosurgery'
)
ORDER BY
    DepartmentName,
    PublicationYear DESC,
    PublicationKey DESC;
