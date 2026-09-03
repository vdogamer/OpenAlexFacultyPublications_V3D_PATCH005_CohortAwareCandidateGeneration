/*
V3E PATCH019A — Department Cache Completeness Diagnostics
*/

SET NOCOUNT ON;

SELECT TOP (20)
    AcademicOnlineSyncRunId,
    DepartmentName,
    FromYear,
    ToYear,
    VerifiedOnly,
    RunStatus,
    ScholarsDiscovered,
    ScholarsProcessed,
    MaxScholarsToProcess,
    ScholarPaginationSafeguardHit,
    PublicationPaginationSafeguardHitCount,
    IsCacheComplete,
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
    d.CanonicalName AS DepartmentName,
    LocalScholarCount = COUNT(DISTINCT sd.AcademicOnlineAuthorId),
    LocalPublicationCount = COUNT(DISTINCT sp.PublicationKey),
    LatestCompletedRunId = latest.AcademicOnlineSyncRunId,
    latest.ScholarsDiscovered,
    latest.ScholarsProcessed,
    latest.MaxScholarsToProcess,
    latest.ScholarPaginationSafeguardHit,
    latest.PublicationPaginationSafeguardHitCount,
    latest.IsCacheComplete,
    latest.FromYear,
    latest.ToYear,
    latest.VerifiedOnly,
    latest.CompletedAtUtc
FROM academiconline.Department d
LEFT JOIN academiconline.ScholarDepartment sd
    ON sd.DepartmentId = d.DepartmentId
LEFT JOIN academiconline.ScholarPublication sp
    ON sp.AcademicOnlineAuthorId = sd.AcademicOnlineAuthorId
OUTER APPLY
(
    SELECT TOP (1)
        r.AcademicOnlineSyncRunId,
        r.ScholarsDiscovered,
        r.ScholarsProcessed,
        r.MaxScholarsToProcess,
        r.ScholarPaginationSafeguardHit,
        r.PublicationPaginationSafeguardHitCount,
        r.IsCacheComplete,
        r.FromYear,
        r.ToYear,
        r.VerifiedOnly,
        r.CompletedAtUtc
    FROM academiconline.SyncRun r
    WHERE r.DepartmentName = d.SourceName
      AND r.RunStatus = N'Completed'
    ORDER BY
        r.CompletedAtUtc DESC,
        r.AcademicOnlineSyncRunId DESC
) latest
WHERE d.IsActive = 1
GROUP BY
    d.DepartmentId,
    d.CanonicalName,
    latest.AcademicOnlineSyncRunId,
    latest.ScholarsDiscovered,
    latest.ScholarsProcessed,
    latest.MaxScholarsToProcess,
    latest.ScholarPaginationSafeguardHit,
    latest.PublicationPaginationSafeguardHitCount,
    latest.IsCacheComplete,
    latest.FromYear,
    latest.ToYear,
    latest.VerifiedOnly,
    latest.CompletedAtUtc
ORDER BY d.CanonicalName;
