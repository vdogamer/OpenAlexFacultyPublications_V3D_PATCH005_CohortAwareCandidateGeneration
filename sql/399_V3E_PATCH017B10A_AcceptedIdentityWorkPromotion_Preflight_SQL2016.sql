/*
V3E PATCH017B10A - Accepted-Identity Work Promotion Preflight
SQL Server 2016 compatible.

Purpose
-------
Read-only preflight for promoting already-fetched OpenAlex work JSON from
openalex.FacultyIdentityCandidateWork into the normalized openalex.Work /
openalex.WorkAuthorship pipeline for the five-faculty cohort.

No OpenAlex API calls are made.
No rows are inserted, updated, or deleted.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateWork does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyAuthorMatchDecision does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.Work', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Work does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('tempdb..#AcceptedIdentityExpandedWork') IS NOT NULL
    DROP TABLE #AcceptedIdentityExpandedWork;

;WITH AcceptedIdentity AS
(
    SELECT
        pc.PilotOrder,
        d.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        d.AuthorOpenAlexId
    FROM openalex.PilotFacultyCohort pc
    INNER JOIN openalex.Faculty f
        ON f.FacultyId = pc.FacultyId
    INNER JOIN openalex.FacultyAuthorMatchDecision d
        ON d.FacultyId = pc.FacultyId
       AND d.DecisionStatus = 'Accepted'
       AND d.IsCurrent = 1
    WHERE pc.CohortName = @CohortName
      AND pc.IsEnabled = 1
),
Expanded AS
(
    SELECT
        ai.PilotOrder,
        ai.FacultyId,
        ai.FacultyDisplayName,
        ai.AuthorOpenAlexId,
        w.WorkOpenAlexId,
        w.WorkOpenAlexShortId,
        w.WorkDisplayName,
        w.WorkDoi,
        w.PublicationYear,
        w.PublicationDate,
        w.WorkType,
        w.WorkRawJson,
        w.AuthorshipsJson,
        w.IngestionRunId,
        ExpandedRowId = w.FacultyIdentityCandidateWorkId
    FROM AcceptedIdentity ai
    INNER JOIN openalex.FacultyIdentityCandidateWork w
        ON w.FacultyId = ai.FacultyId
       AND w.CandidateAuthorOpenAlexId = ai.AuthorOpenAlexId
)
SELECT
    e.*,
    ExistingNormalizedWorkId = nw.WorkId,
    RawJsonAvailable =
        CASE WHEN NULLIF(LTRIM(RTRIM(e.WorkRawJson)), '') IS NULL THEN 0 ELSE 1 END,
    AuthorshipJsonAvailable =
        CASE WHEN NULLIF(LTRIM(RTRIM(e.AuthorshipsJson)), '') IS NULL THEN 0 ELSE 1 END
INTO #AcceptedIdentityExpandedWork
FROM Expanded e
LEFT JOIN openalex.Work nw
    ON nw.OpenAlexId = e.WorkOpenAlexId;

PRINT '1) Accepted-identity expanded-work coverage by faculty';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    CurrentAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.DecisionStatus = 'Accepted'
          AND d.IsCurrent = 1
    ),
    AcceptedIdentityExpandedRows =
    (
        SELECT COUNT(*)
        FROM #AcceptedIdentityExpandedWork x
        WHERE x.FacultyId = f.FacultyId
    ),
    DistinctAcceptedIdentityWorks =
    (
        SELECT COUNT(DISTINCT x.WorkOpenAlexId)
        FROM #AcceptedIdentityExpandedWork x
        WHERE x.FacultyId = f.FacultyId
    ),
    ExistingNormalizedWorks =
    (
        SELECT COUNT(DISTINCT x.WorkOpenAlexId)
        FROM #AcceptedIdentityExpandedWork x
        WHERE x.FacultyId = f.FacultyId
          AND x.ExistingNormalizedWorkId IS NOT NULL
    ),
    MissingNormalizedWorks =
    (
        SELECT COUNT(DISTINCT x.WorkOpenAlexId)
        FROM #AcceptedIdentityExpandedWork x
        WHERE x.FacultyId = f.FacultyId
          AND x.ExistingNormalizedWorkId IS NULL
    ),
    DistinctWorksMissingRawJson =
    (
        SELECT COUNT(DISTINCT x.WorkOpenAlexId)
        FROM #AcceptedIdentityExpandedWork x
        WHERE x.FacultyId = f.FacultyId
          AND x.RawJsonAvailable = 0
    )
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY pc.PilotOrder;

PRINT '2) Duplicate accepted-identity expanded rows for the same faculty/work';
SELECT
    FacultyId,
    FacultyDisplayName,
    WorkOpenAlexId,
    WorkDisplayName,
    ExpandedRowCount = COUNT(*),
    AcceptedAuthorCount = COUNT(DISTINCT AuthorOpenAlexId),
    RawJsonAvailableCount = SUM(RawJsonAvailable)
FROM #AcceptedIdentityExpandedWork
GROUP BY
    FacultyId,
    FacultyDisplayName,
    WorkOpenAlexId,
    WorkDisplayName
HAVING COUNT(*) > 1
ORDER BY FacultyDisplayName, WorkOpenAlexId;

PRINT '3) Missing normalized works proposed for local promotion';
;WITH Ranked AS
(
    SELECT
        x.*,
        PromotionRank =
            ROW_NUMBER() OVER
            (
                PARTITION BY x.WorkOpenAlexId
                ORDER BY
                    x.RawJsonAvailable DESC,
                    x.PilotOrder,
                    x.FacultyId,
                    x.ExpandedRowId DESC
            )
    FROM #AcceptedIdentityExpandedWork x
    WHERE x.ExistingNormalizedWorkId IS NULL
)
SELECT
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    AuthorOpenAlexId,
    WorkOpenAlexId,
    WorkDisplayName,
    WorkDoi,
    PublicationYear,
    RawJsonAvailable,
    AuthorshipJsonAvailable,
    SourceExpandedRowId = ExpandedRowId,
    ProposedAction =
        CASE
            WHEN RawJsonAvailable = 1 THEN 'PromoteThroughWorkIngestionRepository'
            ELSE 'BlockedMissingWorkRawJson'
        END
FROM Ranked
WHERE PromotionRank = 1
ORDER BY PilotOrder, PublicationYear DESC, WorkOpenAlexId;

PRINT '4) PATCH017B10A readiness summary';
DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithExpandedWorks INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM #AcceptedIdentityExpandedWork
);

DECLARE @DistinctAcceptedIdentityWorks INT =
(
    SELECT COUNT(DISTINCT WorkOpenAlexId)
    FROM #AcceptedIdentityExpandedWork
);

DECLARE @ExistingNormalizedWorkCount INT =
(
    SELECT COUNT(DISTINCT WorkOpenAlexId)
    FROM #AcceptedIdentityExpandedWork
    WHERE ExistingNormalizedWorkId IS NOT NULL
);

DECLARE @MissingNormalizedWorkCount INT =
(
    SELECT COUNT(DISTINCT WorkOpenAlexId)
    FROM #AcceptedIdentityExpandedWork
    WHERE ExistingNormalizedWorkId IS NULL
);

DECLARE @MissingRawJsonWorkCount INT =
(
    SELECT COUNT(DISTINCT WorkOpenAlexId)
    FROM #AcceptedIdentityExpandedWork
    WHERE ExistingNormalizedWorkId IS NULL
      AND RawJsonAvailable = 0
);

DECLARE @NormalizedWorkRowsBefore INT =
(
    SELECT COUNT(*) FROM openalex.Work
);

DECLARE @PublicationCandidateRowsBefore INT =
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
         THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END;

DECLARE @PublicationDecisionRowsBefore INT =
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
         THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END;

SELECT
    CohortName = @CohortName,
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithAcceptedIdentityExpandedWorks = @FacultyWithExpandedWorks,
    FacultyWithZeroAcceptedIdentityExpandedWorks =
        @EnabledFacultyCount - @FacultyWithExpandedWorks,
    DistinctAcceptedIdentityWorks = @DistinctAcceptedIdentityWorks,
    ExistingNormalizedWorkCount = @ExistingNormalizedWorkCount,
    MissingNormalizedWorkCount = @MissingNormalizedWorkCount,
    MissingRawJsonWorkCount = @MissingRawJsonWorkCount,
    NormalizedWorkRowsBefore = @NormalizedWorkRowsBefore,
    PublicationCandidateRowsBefore = @PublicationCandidateRowsBefore,
    PublicationDecisionRowsBefore = @PublicationDecisionRowsBefore,
    SafetyStatus = 'PreviewOnly_NoDataChange_NoOpenAlexRequest',
    NextAction =
        CASE
            WHEN @FacultyWithExpandedWorks < @EnabledFacultyCount
                THEN 'ReviewFacultyWithZeroAcceptedIdentityExpandedWorks'
            WHEN @MissingNormalizedWorkCount = 0
                THEN 'RerunPATCH017B10PublicationPreview'
            WHEN @MissingRawJsonWorkCount > 0
                THEN 'RepairMissingWorkRawJsonBeforePromotion'
            ELSE 'ProceedToControlledAcceptedIdentityWorkPromotion'
        END;

DROP TABLE #AcceptedIdentityExpandedWork;
