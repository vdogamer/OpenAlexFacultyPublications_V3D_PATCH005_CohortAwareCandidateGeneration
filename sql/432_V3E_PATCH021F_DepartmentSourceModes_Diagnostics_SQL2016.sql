/*
V3E PATCH021F — Department Source Mode Diagnostics
*/

SET NOCOUNT ON;

SELECT
    SourceMode,
    DepartmentCount = COUNT(*)
FROM academiconline.Department
WHERE IsActive = 1
GROUP BY SourceMode
ORDER BY SourceMode;

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
    d.CanonicalName,
    d.SourceMode,
    d.SourceModeNote,
    d.RefreshEnabled,
    d.RefreshFromYear,
    d.RefreshToYear,
    d.RefreshVerifiedOnly,
    d.RefreshMaxScholars,
    SourceUnitAliasCount =
        (
            SELECT COUNT(*)
            FROM academiconline.DepartmentSourceUnit u
            WHERE u.DepartmentId = d.DepartmentId
              AND u.IsActive = 1
        ),
    RosterMemberCount =
        (
            SELECT COUNT(*)
            FROM academiconline.DepartmentRosterMember r
            WHERE r.DepartmentId = d.DepartmentId
              AND r.IsActive = 1
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
    u.AcademicOnlineSearchName,
    u.SearchPriority,
    u.LastAttemptStatus,
    u.LastScholarCount,
    u.LastError
FROM academiconline.DepartmentSourceUnit u
JOIN academiconline.Department d
    ON d.DepartmentId = u.DepartmentId
WHERE u.IsActive = 1
ORDER BY
    d.CanonicalName,
    u.OfficialUnitName,
    u.SearchPriority;

SELECT
    d.CanonicalName AS DepartmentName,
    r.DisplayName,
    r.ExpectedEmail,
    r.AcademicOnlineAuthorId,
    r.SearchPriority,
    r.ResolutionStatus,
    r.LastResolvedAtUtc,
    r.ResolutionError
FROM academiconline.DepartmentRosterMember r
JOIN academiconline.Department d
    ON d.DepartmentId = r.DepartmentId
WHERE r.IsActive = 1
ORDER BY
    d.CanonicalName,
    r.SearchPriority,
    r.DisplayName;
