/*
V3E PATCH021E — Anchor Resolution Validation
*/

SET NOCOUNT ON;

SELECT TOP (5)
    q.DepartmentDiscoveryRequestId,
    q.RequestStatus,
    q.RequestedAtUtc,
    q.CompletedAtUtc,
    q.DiscoveryRunId,
    r.RunStatus,
    r.PagesFetched,
    r.ScholarsScanned,
    r.AffiliationsObserved,
    r.DepartmentsDiscovered,
    r.DepartmentsInserted,
    r.DepartmentsUpdated,
    r.HitMaxPages,
    r.SummaryText
FROM academiconline.DepartmentDiscoveryRequest q
LEFT JOIN academiconline.DepartmentDiscoveryRun r
    ON r.DepartmentDiscoveryRunId = q.DiscoveryRunId
WHERE q.ParentName = N'School of Medicine'
ORDER BY q.DepartmentDiscoveryRequestId DESC;

SELECT
    DepartmentId,
    CanonicalName AS DepartmentName,
    AcademicOnlineDepartmentId,
    AcademicOnlineSlug,
    ParentName,
    ParentSlug,
    DiscoveryScholarCount,
    DiscoveryLastSeenAtUtc,
    LastDiscoveryRunId,
    RefreshEnabled
FROM academiconline.Department
WHERE
    LOWER(CanonicalName) LIKE N'%medicine%'
    OR LOWER(CanonicalName) LIKE N'%neurosurg%'
ORDER BY CanonicalName;

SELECT TOP (10)
    CreatedAtUtc,
    LogLevel,
    EventName,
    Message
FROM openalex.OperationalLog
WHERE Category = N'AcademicOnlineDepartmentDiscovery'
  AND EventName IN
  (
      N'DepartmentUnresolvedDiagnostic',
      N'DiscoveryFinished'
  )
ORDER BY CreatedAtUtc DESC;
