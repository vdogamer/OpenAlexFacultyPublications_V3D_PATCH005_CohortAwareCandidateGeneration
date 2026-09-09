/*
V3E PATCH021K — DOI-Seeded OpenAlex Identity Recovery Validation
Run after the next Department of Neurosurgery refresh.
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

DECLARE @LatestRunId BIGINT =
(
    SELECT MAX(
        AcademicOnlineSyncRunId)
    FROM academiconline.SyncRun
    WHERE DepartmentName =
        N'Department of Neurosurgery'
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
WHERE DepartmentName =
      N'Department of Neurosurgery'
ORDER BY
    AcademicOnlineSyncRunId DESC;

SELECT
    rm.DisplayName,
    rm.AcademicOnlineAuthorId,
    rm.OpenAlexAuthorId,
    rm.OpenAlexResolutionStatus,
    rm.OpenAlexLastResolvedAtUtc,
    EvidenceLength =
        LEN(
            rm.OpenAlexResolutionEvidence),
    rm.OpenAlexResolutionEvidence
FROM academiconline.DepartmentRosterMember rm
WHERE rm.DepartmentId =
      @DepartmentId
  AND rm.IsActive = 1
ORDER BY
    CASE
        WHEN rm.DisplayName =
             N'Elisa Kucia'
            THEN 0
        ELSE 1
    END,
    rm.SearchPriority,
    rm.DisplayName;

SELECT
    rm.DisplayName,
    seed.SeedType,
    seed.SeedValue,
    seed.OpenAlexWorkId,
    seed.SourceReference,
    seed.SeedPriority,
    seed.IsActive
FROM academiconline.DepartmentRosterIdentitySeed seed
JOIN academiconline.DepartmentRosterMember rm
    ON rm.DepartmentRosterMemberId =
       seed.DepartmentRosterMemberId
WHERE rm.DepartmentId =
      @DepartmentId
ORDER BY
    rm.DisplayName,
    seed.SeedPriority,
    seed.DepartmentRosterIdentitySeedId;

SELECT
    SourceProvider,
    RosterMembersRepresented =
        COUNT(
            DISTINCT
            DepartmentRosterMemberId),
    AttributionRows =
        COUNT(*),
    UniquePublications =
        COUNT(
            DISTINCT
            PublicationKey)
FROM academiconline.DepartmentRosterPublication
WHERE DepartmentId =
      @DepartmentId
  AND LastSyncRunId =
      @LatestRunId
GROUP BY
    SourceProvider
ORDER BY
    SourceProvider;

SELECT
    rm.DisplayName,
    drp.SourceProvider,
    drp.SourceAuthorId,
    PublicationCount =
        COUNT(
            DISTINCT
            drp.PublicationKey),
    MinPublicationYear =
        MIN(
            p.PublicationYear),
    MaxPublicationYear =
        MAX(
            p.PublicationYear)
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.DepartmentRosterMember rm
    ON rm.DepartmentRosterMemberId =
       drp.DepartmentRosterMemberId
JOIN academiconline.Publication p
    ON p.PublicationKey =
       drp.PublicationKey
WHERE drp.DepartmentId =
      @DepartmentId
  AND drp.LastSyncRunId =
      @LatestRunId
GROUP BY
    rm.DisplayName,
    drp.SourceProvider,
    drp.SourceAuthorId
ORDER BY
    rm.DisplayName,
    drp.SourceProvider;

SELECT
    d.DepartmentId,
    d.CanonicalName,
    d.RefreshEnabled,
    d.LastRefreshAttemptStatus,
    d.LastRefreshAttemptRunId,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc
FROM academiconline.Department d
WHERE d.DepartmentId =
      @DepartmentId;

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
WHERE DepartmentName =
      N'Department of Neurosurgery'
ORDER BY
    PublicationYear DESC,
    PublicationKey DESC;
