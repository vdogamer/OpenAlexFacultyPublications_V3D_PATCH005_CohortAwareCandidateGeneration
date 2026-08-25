/*
V3E PATCH017B8A - Five-Faculty Identity Candidate Summary Diagnostics
SQL Server 2016 compatible.

Purpose
-------
Verify the most recent FacultyIdentityCandidateSummaryGeneration run for the
V3E_PATCH017B8_FIVE_FACULTY cohort, inspect identity-review triage outcomes,
and confirm that summary generation did not create author decisions,
publication candidates, or publication decisions.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestRunId BIGINT;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateSummary does not exist. Run sql/230_V3E_PATCH004_FacultyIdentityCandidateSummary.sql first.', 16, 1);
    RETURN;
END;

SELECT TOP (1)
    @LatestRunId = ir.IngestionRunId
FROM openalex.IngestionRun ir
WHERE ir.InstitutionFilter LIKE N'FacultyIdentityCandidateSummaryGeneration%'
  AND ir.InstitutionFilter LIKE N'%PilotCohortName=' + @CohortName + N'%'
ORDER BY ir.IngestionRunId DESC;

IF @LatestRunId IS NULL
BEGIN
    RAISERROR('No FacultyIdentityCandidateSummaryGeneration run was found for the PATCH017B8 cohort.', 16, 1);
    RETURN;
END;

PRINT '1) Latest identity-summary generation run';
SELECT
    ir.IngestionRunId,
    ir.RunMode,
    ir.Status,
    ir.InstitutionFilter,
    ir.StartedBy,
    ir.ApplicationVersion,
    ir.StartedAtUtc,
    ir.FinishedAtUtc,
    ir.ErrorCount,
    irs.AuthorsProcessed,
    irs.WorksProcessed,
    irs.SummaryText
FROM openalex.IngestionRun ir
LEFT JOIN openalex.IngestionRunSummary irs
    ON irs.IngestionRunId = ir.IngestionRunId
WHERE ir.IngestionRunId = @LatestRunId
ORDER BY irs.CreatedAtUtc DESC;

PRINT '2) Summary/triage counts by faculty';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    SummaryRows = COUNT(s.FacultyIdentityCandidateSummaryId),
    LikelyIdentityCount = SUM(CASE WHEN s.IdentityReviewTriageStatus = 'LikelyIdentity' THEN 1 ELSE 0 END),
    PossibleSplitProfileCount = SUM(CASE WHEN s.IdentityReviewTriageStatus = 'PossibleSplitProfile' THEN 1 ELSE 0 END),
    NeedsManualReviewCount = SUM(CASE WHEN s.IdentityReviewTriageStatus = 'NeedsManualReview' THEN 1 ELSE 0 END),
    WeakCandidateCount = SUM(CASE WHEN s.IdentityReviewTriageStatus = 'WeakCandidate' THEN 1 ELSE 0 END),
    LikelyFalsePositiveCount = SUM(CASE WHEN s.IdentityReviewTriageStatus = 'LikelyFalsePositive' THEN 1 ELSE 0 END),
    BestIdentityEvidenceScore = MAX(s.BestIdentityEvidenceScore),
    BestWorkEvidenceScore = MAX(s.BestWorkEvidenceScore)
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
LEFT JOIN openalex.FacultyIdentityCandidateSummary s
    ON s.FacultyId = pc.FacultyId
   AND s.IngestionRunId = @LatestRunId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
GROUP BY pc.PilotOrder, f.FacultyId, f.DisplayName
ORDER BY pc.PilotOrder;

PRINT '3) Candidate summary details for human identity review';
SELECT
    pc.PilotOrder,
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.BestCandidateType,
    s.BestIdentityEvidenceScore,
    s.BestIdentityEvidenceStatus,
    s.DiscoveryMethods,
    s.DistinctExpandedWorks,
    s.ScoredWorkRows,
    s.StrongWorkEvidenceRows,
    s.ModerateWorkEvidenceRows,
    s.WeakWorkEvidenceRows,
    s.ConflictingWorkEvidenceRows,
    s.AverageWorkEvidenceScore,
    s.BestWorkEvidenceScore,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority
FROM openalex.FacultyIdentityCandidateSummary s
INNER JOIN openalex.PilotFacultyCohort pc
    ON pc.FacultyId = s.FacultyId
   AND pc.CohortName = @CohortName
   AND pc.IsEnabled = 1
WHERE s.IngestionRunId = @LatestRunId
ORDER BY
    pc.PilotOrder,
    s.ReviewPriority,
    s.BestIdentityEvidenceScore DESC,
    s.BestWorkEvidenceScore DESC,
    s.CandidateAuthorOpenAlexId;

PRINT '4) Matching-data safety check';
SELECT
    CurrentAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d
        INNER JOIN openalex.PilotFacultyCohort pc ON pc.FacultyId = d.FacultyId
        WHERE pc.CohortName = @CohortName
          AND pc.IsEnabled = 1
          AND d.DecisionStatus = 'Accepted'
          AND d.IsCurrent = 1
    ),
    PublicationCandidateCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationCandidate c
        INNER JOIN openalex.PilotFacultyCohort pc ON pc.FacultyId = c.FacultyId
        WHERE pc.CohortName = @CohortName
          AND pc.IsEnabled = 1
    ),
    CurrentPublicationDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationDecision d
        INNER JOIN openalex.PilotFacultyCohort pc ON pc.FacultyId = d.FacultyId
        WHERE pc.CohortName = @CohortName
          AND pc.IsEnabled = 1
          AND d.IsCurrent = 1
    ),
    SafetyStatus = 'IdentitySummaryOnly_NoDecisionsOrPublicationCandidatesExpected';

PRINT '5) Identity-review readiness summary';
DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithSummaries INT =
(
    SELECT COUNT(DISTINCT s.FacultyId)
    FROM openalex.FacultyIdentityCandidateSummary s
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = s.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE s.IngestionRunId = @LatestRunId
);

DECLARE @SummaryRowCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityCandidateSummary s
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = s.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE s.IngestionRunId = @LatestRunId
);

DECLARE @ActionableSummaryCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityCandidateSummary s
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = s.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE s.IngestionRunId = @LatestRunId
      AND s.IdentityReviewTriageStatus IN ('LikelyIdentity', 'PossibleSplitProfile', 'NeedsManualReview')
);

DECLARE @LatestRunStatus VARCHAR(50) =
(
    SELECT Status FROM openalex.IngestionRun WHERE IngestionRunId = @LatestRunId
);

SELECT
    LatestRunId = @LatestRunId,
    LatestRunStatus = @LatestRunStatus,
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithSummaries = @FacultyWithSummaries,
    FacultyWithZeroSummaries = @EnabledFacultyCount - @FacultyWithSummaries,
    SummaryRowCount = @SummaryRowCount,
    ActionableSummaryCount = @ActionableSummaryCount,
    NextAction =
        CASE
            WHEN @LatestRunStatus = 'Completed'
             AND @FacultyWithSummaries = @EnabledFacultyCount
             AND @SummaryRowCount > 0
                THEN 'OpenIdentityReviewWorkflowForFiveFacultyCohort'
            WHEN @LatestRunStatus <> 'Completed'
                THEN 'InspectLatestIdentitySummaryGenerationRunFailure'
            ELSE 'ReviewFacultyWithZeroSummariesBeforeIdentityReview'
        END;
