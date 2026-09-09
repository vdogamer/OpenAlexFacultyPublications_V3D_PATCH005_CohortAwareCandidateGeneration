/*
V3E PATCH021M — Local Publication-Year Policy Validation
Run after the next Department of Neurosurgery refresh.
Read-only. SQL Server 2016 compatible.
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

DECLARE @CurrentSnapshotRunId BIGINT =
(
    SELECT CurrentSnapshotRunId
    FROM academiconline.Department
    WHERE DepartmentId = @DepartmentId
);

DECLARE @RefreshFromYear INT =
(
    SELECT RefreshFromYear
    FROM academiconline.Department
    WHERE DepartmentId = @DepartmentId
);

DECLARE @RefreshToYear INT =
(
    SELECT RefreshToYear
    FROM academiconline.Department
    WHERE DepartmentId = @DepartmentId
);


SELECT
    d.DepartmentId,
    d.CanonicalName,
    d.SourceMode,
    d.RefreshFromYear,
    d.RefreshToYear,
    d.RefreshVerifiedOnly,
    d.LastRefreshAttemptStatus,
    d.LastRefreshAttemptRunId,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc
FROM academiconline.Department d
WHERE d.DepartmentId =
      @DepartmentId;

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
    CurrentReportRows =
        COUNT(*),
    CurrentReportNullYears =
        SUM
        (
            CASE
                WHEN PublicationYear IS NULL
                    THEN 1
                ELSE 0
            END
        ),
    CurrentReportInvalidYears =
        SUM
        (
            CASE
                WHEN PublicationYear IS NOT NULL
                 AND
                 (
                     PublicationYear < 1000
                     OR PublicationYear > 2100
                 )
                    THEN 1
                ELSE 0
            END
        ),
    CurrentReportOutsideYearPolicy =
        SUM
        (
            CASE
                WHEN
                    (
                        @RefreshFromYear IS NOT NULL
                        OR @RefreshToYear IS NOT NULL
                    )
                 AND PublicationYear IS NULL
                    THEN 1
                WHEN @RefreshFromYear IS NOT NULL
                 AND PublicationYear <
                     @RefreshFromYear
                    THEN 1
                WHEN @RefreshToYear IS NOT NULL
                 AND PublicationYear >
                     @RefreshToYear
                    THEN 1
                ELSE 0
            END
        )
FROM academiconline.vw_DepartmentNewestPublication
WHERE DepartmentName =
      N'Department of Neurosurgery';

SELECT
    CurrentDepartmentRowsOutsideYearPolicy =
        COUNT(*)
FROM academiconline.DepartmentPublication dp
JOIN academiconline.Publication p
    ON p.PublicationKey =
       dp.PublicationKey
JOIN academiconline.Department d
    ON d.DepartmentId =
       dp.DepartmentId
WHERE dp.DepartmentId =
      @DepartmentId
  AND dp.IsCurrent = 1
  AND dp.CurrentSnapshotRunId =
      @CurrentSnapshotRunId
  AND
  (
      (
          d.RefreshFromYear IS NOT NULL
          OR d.RefreshToYear IS NOT NULL
      )
      AND
      (
          p.PublicationYear IS NULL
          OR
          (
              d.RefreshFromYear IS NOT NULL
              AND p.PublicationYear <
                  d.RefreshFromYear
          )
          OR
          (
              d.RefreshToYear IS NOT NULL
              AND p.PublicationYear >
                  d.RefreshToYear
          )
      )
  );

SELECT
    CurrentRosterRowsOutsideYearPolicy =
        COUNT(*)
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.Publication p
    ON p.PublicationKey =
       drp.PublicationKey
JOIN academiconline.Department d
    ON d.DepartmentId =
       drp.DepartmentId
WHERE drp.DepartmentId =
      @DepartmentId
  AND drp.IsCurrent = 1
  AND drp.CurrentSnapshotRunId =
      @CurrentSnapshotRunId
  AND
  (
      (
          d.RefreshFromYear IS NOT NULL
          OR d.RefreshToYear IS NOT NULL
      )
      AND
      (
          p.PublicationYear IS NULL
          OR
          (
              d.RefreshFromYear IS NOT NULL
              AND p.PublicationYear <
                  d.RefreshFromYear
          )
          OR
          (
              d.RefreshToYear IS NOT NULL
              AND p.PublicationYear >
                  d.RefreshToYear
          )
      )
  );

SELECT
    SourceProvider,
    CurrentRosterMembersRepresented =
        COUNT(DISTINCT DepartmentRosterMemberId),
    CurrentAttributionRows =
        COUNT(*),
    CurrentUniquePublications =
        COUNT(DISTINCT PublicationKey)
FROM academiconline.DepartmentRosterPublication
WHERE DepartmentId =
      @DepartmentId
  AND IsCurrent = 1
  AND CurrentSnapshotRunId =
      @CurrentSnapshotRunId
GROUP BY
    SourceProvider
ORDER BY
    SourceProvider;

SELECT
    GlobalNullPublicationYears =
        SUM
        (
            CASE
                WHEN PublicationYear IS NULL
                    THEN 1
                ELSE 0
            END
        ),
    GlobalInvalidPublicationYears =
        SUM
        (
            CASE
                WHEN PublicationYear IS NOT NULL
                 AND
                 (
                     PublicationYear < 1000
                     OR PublicationYear > 2100
                 )
                    THEN 1
                ELSE 0
            END
        ),
    GlobalNegativeCitationCounts =
        SUM
        (
            CASE
                WHEN CitationCount < 0
                    THEN 1
                ELSE 0
            END
        )
FROM academiconline.Publication;

SELECT TOP (25)
    p.PublicationKey,
    p.AcademicOnlinePublicationId,
    p.OpenAlexId,
    p.Doi,
    p.YearText,
    p.PublicationYear,
    p.Title,
    dp.IsCurrent,
    dp.CurrentSnapshotRunId,
    dp.LastSyncRunId
FROM academiconline.DepartmentPublication dp
JOIN academiconline.Publication p
    ON p.PublicationKey =
       dp.PublicationKey
WHERE dp.DepartmentId =
      @DepartmentId
  AND p.PublicationYear IS NULL
ORDER BY
    dp.IsCurrent DESC,
    p.LastRetrievedAtUtc DESC,
    p.PublicationKey DESC;
