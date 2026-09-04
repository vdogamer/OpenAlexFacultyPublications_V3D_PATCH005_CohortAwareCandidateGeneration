/*
V3E PATCH021 — Department Discovery Diagnostics
*/

SET NOCOUNT ON;

SELECT TOP (10)
    DepartmentDiscoveryRequestId,
    ParentName,
    RowsPerPage,
    MaxPages,
    RequestStatus,
    RequestedBy,
    RequestedAtUtc,
    StartedAtUtc,
    CompletedAtUtc,
    LeaseOwner,
    LeaseUntilUtc,
    DiscoveryRunId,
    ErrorText
FROM academiconline.DepartmentDiscoveryRequest
ORDER BY DepartmentDiscoveryRequestId DESC;

SELECT TOP (10)
    DepartmentDiscoveryRunId,
    DepartmentDiscoveryRequestId,
    ParentName,
    RunStatus,
    PagesFetched,
    ScholarsScanned,
    AffiliationsObserved,
    DepartmentsDiscovered,
    DepartmentsInserted,
    DepartmentsUpdated,
    HitMaxPages,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CompletedAtUtc,
    SummaryText,
    ErrorText
FROM academiconline.DepartmentDiscoveryRun
ORDER BY DepartmentDiscoveryRunId DESC;

SELECT
    DepartmentId,
    CanonicalName AS DepartmentName,
    AcademicOnlineDepartmentId,
    AcademicOnlineSlug,
    ParentAcademicOnlineDepartmentId,
    ParentName,
    ParentSlug,
    DiscoveryScholarCount,
    DiscoveryFirstSeenAtUtc,
    DiscoveryLastSeenAtUtc,
    LastDiscoveryRunId,
    RefreshEnabled,
    RefreshFromYear,
    RefreshToYear,
    RefreshVerifiedOnly,
    RefreshMaxScholars,
    CurrentSnapshotRunId
FROM academiconline.Department
ORDER BY CanonicalName;
