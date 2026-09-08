/*
V3E PATCH021J — Mixed-Source Faculty-Roster Publication Validation
Run after the next Department of Neurosurgery refresh.
*/

SET NOCOUNT ON;

DECLARE @DepartmentId BIGINT =
(
    SELECT TOP (1)
        DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName = N'Department of Neurosurgery'
      AND IsActive = 1
    ORDER BY DepartmentId
);

DECLARE @LatestRunId BIGINT =
(
    SELECT MAX(AcademicOnlineSyncRunId)
    FROM academiconline.SyncRun
    WHERE DepartmentName = N'Department of Neurosurgery'
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
WHERE DepartmentName = N'Department of Neurosurgery'
ORDER BY AcademicOnlineSyncRunId DESC;

SELECT
    TotalRosterMembers = COUNT(*),
    AcademicOnlineIdentities =
        SUM(CASE WHEN AcademicOnlineAuthorId IS NOT NULL THEN 1 ELSE 0 END),
    OpenAlexFallbackIdentities =
        SUM
        (
            CASE
                WHEN AcademicOnlineAuthorId IS NULL
                 AND OpenAlexAuthorId IS NOT NULL
                 AND OpenAlexResolutionStatus = N'ProposedUnique'
                THEN 1
                ELSE 0
            END
        ),
    StillUnresolved =
        SUM
        (
            CASE
                WHEN AcademicOnlineAuthorId IS NULL
                 AND
                 (
                     OpenAlexAuthorId IS NULL
                     OR OpenAlexResolutionStatus <> N'ProposedUnique'
                 )
                THEN 1
                ELSE 0
            END
        )
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1;

SELECT
    drp.SourceProvider,
    RosterMembersRepresented =
        COUNT(DISTINCT drp.DepartmentRosterMemberId),
    PublicationAttributionRows =
        COUNT(*),
    UniquePublications =
        COUNT(DISTINCT drp.PublicationKey),
    MinPublicationYear =
        MIN(p.PublicationYear),
    MaxPublicationYear =
        MAX(p.PublicationYear)
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.Publication p
    ON p.PublicationKey = drp.PublicationKey
WHERE drp.DepartmentId = @DepartmentId
  AND drp.LastSyncRunId = @LatestRunId
GROUP BY drp.SourceProvider
ORDER BY drp.SourceProvider;

SELECT
    rm.DisplayName,
    drp.SourceProvider,
    drp.SourceAuthorId,
    PublicationCount =
        COUNT(DISTINCT drp.PublicationKey),
    MinPublicationYear =
        MIN(p.PublicationYear),
    MaxPublicationYear =
        MAX(p.PublicationYear)
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.DepartmentRosterMember rm
    ON rm.DepartmentRosterMemberId =
       drp.DepartmentRosterMemberId
JOIN academiconline.Publication p
    ON p.PublicationKey = drp.PublicationKey
WHERE drp.DepartmentId = @DepartmentId
  AND drp.LastSyncRunId = @LatestRunId
GROUP BY
    rm.DisplayName,
    drp.SourceProvider,
    drp.SourceAuthorId
ORDER BY
    drp.SourceProvider,
    rm.DisplayName;

SELECT TOP (100)
    rm.DisplayName,
    drp.SourceProvider,
    drp.SourceAuthorId,
    p.PublicationYear,
    p.Title,
    p.Venue,
    p.Doi,
    p.OpenAlexId,
    p.Verified,
    p.AcademicOnlinePublicationId,
    p.LastRetrievedAtUtc
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.DepartmentRosterMember rm
    ON rm.DepartmentRosterMemberId =
       drp.DepartmentRosterMemberId
JOIN academiconline.Publication p
    ON p.PublicationKey =
       drp.PublicationKey
WHERE drp.DepartmentId = @DepartmentId
  AND drp.LastSyncRunId = @LatestRunId
  AND drp.SourceProvider = N'OpenAlex'
ORDER BY
    rm.DisplayName,
    p.PublicationYear DESC,
    p.PublicationKey DESC;

SELECT
    OpenAlexOnlyRowsWithNonNullVerified =
        COUNT(*)
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.Publication p
    ON p.PublicationKey = drp.PublicationKey
WHERE drp.DepartmentId = @DepartmentId
  AND drp.LastSyncRunId = @LatestRunId
  AND drp.SourceProvider = N'OpenAlex'
  AND p.AcademicOnlinePublicationId IS NULL
  AND p.Verified IS NOT NULL;

SELECT
    DuplicateOpenAlexIds =
        COUNT(*)
FROM
(
    SELECT
        p.OpenAlexId
    FROM academiconline.Publication p
    WHERE p.OpenAlexId IS NOT NULL
    GROUP BY p.OpenAlexId
    HAVING COUNT(*) > 1
) duplicates;

SELECT
    d.DepartmentId,
    d.CanonicalName,
    d.RefreshEnabled,
    d.LastRefreshAttemptStatus,
    d.LastRefreshAttemptRunId,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc
FROM academiconline.Department d
WHERE d.DepartmentId = @DepartmentId;

SELECT
    rm.DisplayName,
    rm.AcademicOnlineAuthorId,
    rm.OpenAlexAuthorId,
    rm.OpenAlexResolutionStatus
FROM academiconline.DepartmentRosterMember rm
WHERE rm.DepartmentId = @DepartmentId
  AND rm.IsActive = 1
ORDER BY
    CASE
        WHEN rm.AcademicOnlineAuthorId IS NULL
         AND
         (
             rm.OpenAlexAuthorId IS NULL
             OR rm.OpenAlexResolutionStatus <> N'ProposedUnique'
         )
        THEN 0
        ELSE 1
    END,
    rm.SearchPriority,
    rm.DisplayName;
