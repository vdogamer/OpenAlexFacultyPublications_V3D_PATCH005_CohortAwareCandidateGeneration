/*
V3E PATCH017B10 - Five-Faculty Publication Candidate Preview
SQL Server 2016 compatible.

Purpose
-------
Read-only cohort-scale preview of publication candidates sourced from current
accepted FacultyAuthorMatchDecision rows.

Important cohort-scale behavior
-------------------------------
A faculty member can legitimately have multiple accepted OpenAlex identities.
The existing candidate-source join also permits ORCID matches. When accepted
identities share an ORCID, the same faculty/work can therefore appear more than
once in the raw source set.

PATCH017B10 detects those collisions and creates one deterministic preview row
per FacultyId + WorkId, preferring:
  1. exact AuthorOpenAlexId match,
  2. corresponding authorship,
  3. stronger institution evidence,
  4. latest accepted author decision.

This script does NOT insert/update/delete publication candidates or decisions.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL
   OR OBJECT_ID(N'openalex.WorkAuthorship', N'U') IS NULL
   OR OBJECT_ID(N'openalex.Work', N'U') IS NULL
BEGIN
    RAISERROR('Required accepted-author/work tables do not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('tempdb..#RawPublicationSource') IS NOT NULL
    DROP TABLE #RawPublicationSource;

IF OBJECT_ID('tempdb..#PublicationPreview') IS NOT NULL
    DROP TABLE #PublicationPreview;

DECLARE @CandidateRowsBefore INT =
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
         THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END;

DECLARE @DecisionRowsBefore INT =
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
         THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END;

;WITH CohortAcceptedDecision AS
(
    SELECT
        pc.PilotOrder,
        d.FacultyAuthorMatchDecisionId,
        d.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        d.AuthorId,
        d.AuthorOpenAlexId,
        d.AuthorDisplayName,
        d.AuthorOrcid
    FROM openalex.PilotFacultyCohort pc
    INNER JOIN openalex.Faculty f
        ON f.FacultyId = pc.FacultyId
    INNER JOIN openalex.FacultyAuthorMatchDecision d
        ON d.FacultyId = pc.FacultyId
       AND d.DecisionStatus = 'Accepted'
       AND d.IsCurrent = 1
    WHERE pc.CohortName = @CohortName
      AND pc.IsEnabled = 1
)
SELECT
    cad.PilotOrder,
    cad.FacultyAuthorMatchDecisionId,
    cad.FacultyId,
    cad.FacultyDisplayName,
    cad.AuthorId,
    cad.AuthorOpenAlexId,
    cad.AuthorDisplayName,
    cad.AuthorOrcid,
    w.WorkId,
    w.OpenAlexId AS WorkOpenAlexId,
    w.OpenAlexShortId AS WorkOpenAlexShortId,
    COALESCE(w.Title, w.DisplayName) AS WorkTitle,
    w.Doi AS WorkDoi,
    w.PublicationYear,
    w.PublicationDate,
    w.WorkType,
    wa.WorkAuthorshipId,
    wa.OrdinalPosition,
    wa.IsCorresponding,
    AuthorshipInstitutionCount =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    MatchingInstitutionCount =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        INNER JOIN openalex.Institution i
            ON i.OpenAlexId = wai.InstitutionOpenAlexId
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    MatchMethod =
        CASE
            WHEN wa.AuthorOpenAlexId = cad.AuthorOpenAlexId
                THEN 'ExactAuthorOpenAlexId'
            WHEN cad.AuthorOrcid IS NOT NULL
             AND wa.AuthorOrcid IS NOT NULL
             AND wa.AuthorOrcid = cad.AuthorOrcid
                THEN 'ExactOrcid'
            ELSE 'Unexpected'
        END,
    ExistingCandidateId =
    (
        SELECT TOP (1)
            c.FacultyPublicationCandidateId
        FROM openalex.FacultyPublicationCandidate c
        WHERE c.FacultyId = cad.FacultyId
          AND c.WorkId = w.WorkId
          AND c.CandidateSource = 'AcceptedAuthorMatch'
        ORDER BY c.FacultyPublicationCandidateId DESC
    )
INTO #RawPublicationSource
FROM CohortAcceptedDecision cad
INNER JOIN openalex.WorkAuthorship wa
    ON
    (
        wa.AuthorOpenAlexId = cad.AuthorOpenAlexId
        OR
        (
            cad.AuthorOrcid IS NOT NULL
            AND wa.AuthorOrcid IS NOT NULL
            AND wa.AuthorOrcid = cad.AuthorOrcid
        )
    )
INNER JOIN openalex.Work w
    ON w.WorkId = wa.WorkId;

;WITH Ranked AS
(
    SELECT
        r.*,
        RawMatchCountForFacultyWork =
            COUNT(*) OVER (PARTITION BY r.FacultyId, r.WorkId),
        PreviewSelectionRank =
            ROW_NUMBER() OVER
            (
                PARTITION BY r.FacultyId, r.WorkId
                ORDER BY
                    CASE WHEN r.MatchMethod = 'ExactAuthorOpenAlexId' THEN 0 ELSE 1 END,
                    CASE WHEN r.IsCorresponding = 1 THEN 0 ELSE 1 END,
                    r.MatchingInstitutionCount DESC,
                    r.AuthorshipInstitutionCount DESC,
                    r.FacultyAuthorMatchDecisionId DESC,
                    r.WorkAuthorshipId
            )
    FROM #RawPublicationSource r
)
SELECT
    PilotOrder,
    FacultyAuthorMatchDecisionId,
    FacultyId,
    FacultyDisplayName,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    WorkAuthorshipId,
    OrdinalPosition,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    MatchMethod,
    RawMatchCountForFacultyWork,
    ExistingCandidateId,
    ProposedCandidateSource = CAST('AcceptedAuthorMatch' AS VARCHAR(100)),
    ProposedCandidateStatus = CAST('PendingReview' AS VARCHAR(50)),
    ProposedBaseMatchScore =
        CAST
        (
            CASE
                WHEN
                    80
                    + CASE WHEN IsCorresponding = 1 THEN 5 ELSE 0 END
                    + CASE WHEN MatchingInstitutionCount > 0 THEN 10 ELSE 0 END
                    + CASE WHEN AuthorshipInstitutionCount > 0 THEN 5 ELSE 0 END
                    > 100
                    THEN 100
                ELSE
                    80
                    + CASE WHEN IsCorresponding = 1 THEN 5 ELSE 0 END
                    + CASE WHEN MatchingInstitutionCount > 0 THEN 10 ELSE 0 END
                    + CASE WHEN AuthorshipInstitutionCount > 0 THEN 5 ELSE 0 END
            END
            AS DECIMAL(9,4)
        ),
    PreviewStatus =
        CAST
        (
            CASE
                WHEN ExistingCandidateId IS NOT NULL
                 AND RawMatchCountForFacultyWork > 1
                    THEN 'ExistingCandidate_DeduplicatedIdentityMatches'
                WHEN ExistingCandidateId IS NOT NULL
                    THEN 'ExistingCandidateWouldUpdate'
                WHEN RawMatchCountForFacultyWork > 1
                    THEN 'ReadyToGenerate_DeduplicatedIdentityMatches'
                ELSE 'ReadyToGenerate'
            END
            AS VARCHAR(100)
        )
INTO #PublicationPreview
FROM Ranked
WHERE PreviewSelectionRank = 1;

PRINT '1) Current accepted-author publication-source coverage by faculty';
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
    RawPublicationSourceRows =
    (
        SELECT COUNT(*)
        FROM #RawPublicationSource r
        WHERE r.FacultyId = f.FacultyId
    ),
    DistinctMatchedWorks =
    (
        SELECT COUNT(DISTINCT r.WorkId)
        FROM #RawPublicationSource r
        WHERE r.FacultyId = f.FacultyId
    ),
    DeduplicatedPreviewRows =
    (
        SELECT COUNT(*)
        FROM #PublicationPreview p
        WHERE p.FacultyId = f.FacultyId
    )
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY pc.PilotOrder;

PRINT '2) Duplicate faculty/work source collisions';
SELECT
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    RawMatchCount = COUNT(*),
    ExactAuthorIdMatchCount =
        SUM(CASE WHEN MatchMethod = 'ExactAuthorOpenAlexId' THEN 1 ELSE 0 END),
    ExactOrcidMatchCount =
        SUM(CASE WHEN MatchMethod = 'ExactOrcid' THEN 1 ELSE 0 END)
FROM #RawPublicationSource
GROUP BY
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle
HAVING COUNT(*) > 1
ORDER BY FacultyDisplayName, WorkId;

PRINT '3) Deduplicated publication-candidate preview';
SELECT
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    MatchMethod,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    RawMatchCountForFacultyWork,
    ProposedBaseMatchScore,
    ExistingCandidateId,
    ProposedCandidateSource,
    ProposedCandidateStatus,
    PreviewStatus
FROM #PublicationPreview
ORDER BY
    PilotOrder,
    PublicationYear DESC,
    WorkId DESC;

PRINT '4) Preview counts by faculty';
SELECT
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    PreviewCandidateCount = COUNT(*),
    NewCandidateCount =
        SUM(CASE WHEN ExistingCandidateId IS NULL THEN 1 ELSE 0 END),
    ExistingCandidateCount =
        SUM(CASE WHEN ExistingCandidateId IS NOT NULL THEN 1 ELSE 0 END),
    DeduplicatedCollisionCandidateCount =
        SUM(CASE WHEN RawMatchCountForFacultyWork > 1 THEN 1 ELSE 0 END),
    BaseScore100Count =
        SUM(CASE WHEN ProposedBaseMatchScore = 100 THEN 1 ELSE 0 END),
    BaseScore95Count =
        SUM(CASE WHEN ProposedBaseMatchScore = 95 THEN 1 ELSE 0 END),
    BaseScore85Count =
        SUM(CASE WHEN ProposedBaseMatchScore = 85 THEN 1 ELSE 0 END),
    BaseScore80Count =
        SUM(CASE WHEN ProposedBaseMatchScore = 80 THEN 1 ELSE 0 END)
FROM #PublicationPreview
GROUP BY PilotOrder, FacultyId, FacultyDisplayName
ORDER BY PilotOrder;

PRINT '5) PATCH017B10 safety/readiness summary';
DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithAcceptedAuthorDecision INT =
(
    SELECT COUNT(DISTINCT d.FacultyId)
    FROM openalex.FacultyAuthorMatchDecision d
    WHERE d.DecisionStatus = 'Accepted'
      AND d.IsCurrent = 1
      AND EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.CohortName = @CohortName
            AND pc.IsEnabled = 1
            AND pc.FacultyId = d.FacultyId
      )
);

DECLARE @FacultyWithPreviewRows INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM #PublicationPreview
);

DECLARE @RawSourceRowCount INT =
(
    SELECT COUNT(*) FROM #RawPublicationSource
);

DECLARE @DistinctFacultyWorkCount INT =
(
    SELECT COUNT(*) FROM #PublicationPreview
);

DECLARE @DuplicateFacultyWorkCount INT =
(
    SELECT COUNT(*)
    FROM
    (
        SELECT FacultyId, WorkId
        FROM #RawPublicationSource
        GROUP BY FacultyId, WorkId
        HAVING COUNT(*) > 1
    ) x
);

DECLARE @DuplicateRawRowsRemoved INT =
    @RawSourceRowCount - @DistinctFacultyWorkCount;

DECLARE @NewCandidateCount INT =
(
    SELECT COUNT(*)
    FROM #PublicationPreview
    WHERE ExistingCandidateId IS NULL
);

DECLARE @ExistingCandidateCount INT =
(
    SELECT COUNT(*)
    FROM #PublicationPreview
    WHERE ExistingCandidateId IS NOT NULL
);

DECLARE @CandidateRowsAfter INT =
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
         THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END;

DECLARE @DecisionRowsAfter INT =
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
         THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END;

SELECT
    CohortName = @CohortName,
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithAcceptedAuthorDecision = @FacultyWithAcceptedAuthorDecision,
    FacultyWithPreviewRows = @FacultyWithPreviewRows,
    FacultyWithZeroPreviewRows = @EnabledFacultyCount - @FacultyWithPreviewRows,
    RawSourceRowCount = @RawSourceRowCount,
    DeduplicatedPreviewRowCount = @DistinctFacultyWorkCount,
    DuplicateFacultyWorkCount = @DuplicateFacultyWorkCount,
    DuplicateRawRowsRemoved = @DuplicateRawRowsRemoved,
    NewCandidateCount = @NewCandidateCount,
    ExistingCandidateCount = @ExistingCandidateCount,
    CandidateRowsBefore = @CandidateRowsBefore,
    CandidateRowsAfter = @CandidateRowsAfter,
    DecisionRowsBefore = @DecisionRowsBefore,
    DecisionRowsAfter = @DecisionRowsAfter,
    SafetyStatus =
        CASE
            WHEN @CandidateRowsBefore = @CandidateRowsAfter
             AND @DecisionRowsBefore = @DecisionRowsAfter
                THEN 'PreviewOnly_NoPublicationCandidateOrDecisionChange'
            ELSE 'InvestigateUnexpectedPublicationCountChange'
        END,
    NextAction =
        CASE
            WHEN @FacultyWithAcceptedAuthorDecision < @EnabledFacultyCount
                THEN 'RepairAcceptedAuthorDecisionCoverageBeforeCandidateExecution'
            WHEN @DistinctFacultyWorkCount = 0
                THEN 'ReviewMissingLocalWorkCoverageBeforeCandidateExecution'
            WHEN @FacultyWithPreviewRows < @EnabledFacultyCount
                THEN 'ReviewFacultyWithZeroPreviewRowsBeforeCandidateExecution'
            WHEN @CandidateRowsBefore <> @CandidateRowsAfter
              OR @DecisionRowsBefore <> @DecisionRowsAfter
                THEN 'InvestigateUnexpectedPublicationCountChange'
            ELSE 'ProceedToControlledFiveFacultyPublicationCandidateExecution'
        END;

DROP TABLE #PublicationPreview;
DROP TABLE #RawPublicationSource;
