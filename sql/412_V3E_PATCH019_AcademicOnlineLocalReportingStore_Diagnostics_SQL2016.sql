/*
V3E PATCH019 diagnostics — Academic Online local reporting store.
*/

SET NOCOUNT ON;

SELECT
    CurrentDatabase = DB_NAME(),
    SyncRunCount = (SELECT COUNT_BIG(*) FROM academiconline.SyncRun),
    DepartmentCount = (SELECT COUNT_BIG(*) FROM academiconline.Department),
    ScholarCount = (SELECT COUNT_BIG(*) FROM academiconline.Scholar),
    PublicationCount = (SELECT COUNT_BIG(*) FROM academiconline.Publication),
    ScholarPublicationCount = (SELECT COUNT_BIG(*) FROM academiconline.ScholarPublication);

SELECT TOP (20)
    AcademicOnlineSyncRunId,
    SyncType,
    DepartmentName,
    FromYear,
    ToYear,
    VerifiedOnly,
    RunStatus,
    ScholarsProcessed,
    PublicationsProcessed,
    ScholarPagesFetched,
    PublicationPagesFetched,
    StartedAtUtc,
    CompletedAtUtc,
    SummaryText
FROM academiconline.SyncRun
ORDER BY AcademicOnlineSyncRunId DESC;

SELECT
    d.DepartmentId,
    d.CanonicalName,
    ScholarCount = COUNT(DISTINCT sd.AcademicOnlineAuthorId),
    PublicationCount = COUNT(DISTINCT sp.PublicationKey)
FROM academiconline.Department d
LEFT JOIN academiconline.ScholarDepartment sd
    ON sd.DepartmentId = d.DepartmentId
LEFT JOIN academiconline.ScholarPublication sp
    ON sp.AcademicOnlineAuthorId = sd.AcademicOnlineAuthorId
GROUP BY d.DepartmentId, d.CanonicalName
ORDER BY d.CanonicalName;

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
