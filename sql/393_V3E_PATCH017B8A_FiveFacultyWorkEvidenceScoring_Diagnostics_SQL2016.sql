/*
V3E PATCH017B8A - Five-Faculty Work Evidence Scoring Diagnostics
SQL Server 2016 compatible.

Purpose
-------
Verify the most recent FacultyIdentityWorkEvidenceScoring run for the
V3E_PATCH017B8_FIVE_FACULTY cohort, inspect work-evidence distributions, and
confirm that scoring did not create author decisions, publication candidates,
or publication decisions.

Schema note
-----------
AuthorsProcessed, WorksProcessed, and SummaryText are stored in
openalex.IngestionRunSummary, not openalex.IngestionRun.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestRunId BIGINT;

IF OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityWorkEvidenceScore does not exist. Run sql/220_V3E_PATCH003_FacultyIdentityWorkEvidenceScore.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.IngestionRunSummary', N'U') IS NULL
BEGIN
    RAISERROR('openalex.IngestionRunSummary does not exist.', 16, 1);
    RETURN;
END;

SELECT TOP (1)
    @LatestRunId = ir.IngestionRunId
FROM openalex.IngestionRun ir
WHERE ir.InstitutionFilter LIKE N'FacultyIdentityWorkEvidenceScoring%'
  AND ir.InstitutionFilter LIKE N'%PilotCohortName=' + @CohortName + N'%'
ORDER BY ir.IngestionRunId DESC;

IF @LatestRunId IS NULL
BEGIN
    RAISERROR('No FacultyIdentityWorkEvidenceScoring run was found for the PATCH017B8 cohort.', 16, 1);
    RETURN;
END;

PRINT '1) Latest work-evidence scoring run';
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
    irs.SummaryText,
    irs.CreatedAtUtc AS SummaryCreatedAtUtc
FROM openalex.IngestionRun ir
LEFT JOIN openalex.IngestionRunSummary irs
    ON irs.IngestionRunId = ir.IngestionRunId
WHERE ir.IngestionRunId = @LatestRunId
ORDER BY irs.CreatedAtUtc DESC;

PRINT '2) Score counts and evidence distribution by faculty';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    ScoredWorkRows = COUNT(s.WorkOpenAlexId),
    DistinctCandidateAuthors = COUNT(DISTINCT s.CandidateAuthorOpenAlexId),
    StrongWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'StrongWorkEvidence' THEN 1 ELSE 0 END),
    ModerateWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'ModerateWorkEvidence' THEN 1 ELSE 0 END),
    WeakWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'WeakWorkEvidence' THEN 1 ELSE 0 END),
    NeedsManualReviewRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'NeedsManualReview' THEN 1 ELSE 0 END),
    ConflictingWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'ConflictingWorkEvidence' THEN 1 ELSE 0 END),
    AverageWorkEvidenceScore = CAST(AVG(CAST(s.OverallWorkEvidenceScore AS DECIMAL(18,4))) AS DECIMAL(18,4)),
    BestWorkEvidenceScore = MAX(s.OverallWorkEvidenceScore),
    LowestWorkEvidenceScore = MIN(s.OverallWorkEvidenceScore)
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
LEFT JOIN openalex.FacultyIdentityWorkEvidenceScore s
    ON s.FacultyId = pc.FacultyId
   AND s.IngestionRunId = @LatestRunId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
GROUP BY pc.PilotOrder, f.FacultyId, f.DisplayName
ORDER BY pc.PilotOrder;

PRINT '3) Candidate-author evidence summary';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    s.CandidateAuthorOpenAlexId,
    ScoredWorkRows = COUNT(*),
    StrongWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'StrongWorkEvidence' THEN 1 ELSE 0 END),
    ModerateWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'ModerateWorkEvidence' THEN 1 ELSE 0 END),
    WeakWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'WeakWorkEvidence' THEN 1 ELSE 0 END),
    NeedsManualReviewRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'NeedsManualReview' THEN 1 ELSE 0 END),
    ConflictingWorkEvidenceRows = SUM(CASE WHEN s.WorkEvidenceStatus = 'ConflictingWorkEvidence' THEN 1 ELSE 0 END),
    AverageWorkEvidenceScore = CAST(AVG(CAST(s.OverallWorkEvidenceScore AS DECIMAL(18,4))) AS DECIMAL(18,4)),
    BestWorkEvidenceScore = MAX(s.OverallWorkEvidenceScore),
    EarliestPublicationYear = MIN(s.PublicationYear),
    LatestPublicationYear = MAX(s.PublicationYear)
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
INNER JOIN openalex.FacultyIdentityWorkEvidenceScore s
    ON s.FacultyId = pc.FacultyId
   AND s.IngestionRunId = @LatestRunId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
GROUP BY pc.PilotOrder, f.FacultyId, f.DisplayName, s.CandidateAuthorOpenAlexId
ORDER BY pc.PilotOrder, BestWorkEvidenceScore DESC, AverageWorkEvidenceScore DESC, s.CandidateAuthorOpenAlexId;

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
    SafetyStatus = 'EvidenceScoringOnly_NoDecisionsOrPublicationCandidatesExpected';

PRINT '5) Scoring readiness summary';
DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithScores INT =
(
    SELECT COUNT(DISTINCT s.FacultyId)
    FROM openalex.FacultyIdentityWorkEvidenceScore s
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = s.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE s.IngestionRunId = @LatestRunId
);

DECLARE @ScoreRowCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityWorkEvidenceScore s
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = s.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE s.IngestionRunId = @LatestRunId
);

DECLARE @StrongOrModerateRows INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityWorkEvidenceScore s
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = s.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE s.IngestionRunId = @LatestRunId
      AND s.WorkEvidenceStatus IN ('StrongWorkEvidence', 'ModerateWorkEvidence')
);

DECLARE @LatestRunStatus VARCHAR(50) =
(
    SELECT Status FROM openalex.IngestionRun WHERE IngestionRunId = @LatestRunId
);

SELECT
    LatestRunId = @LatestRunId,
    LatestRunStatus = @LatestRunStatus,
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithScoredWorks = @FacultyWithScores,
    FacultyWithZeroScoredWorks = @EnabledFacultyCount - @FacultyWithScores,
    ScoreRowCount = @ScoreRowCount,
    StrongOrModerateWorkEvidenceRows = @StrongOrModerateRows,
    NextAction =
        CASE
            WHEN @LatestRunStatus = 'Completed'
             AND @FacultyWithScores = @EnabledFacultyCount
             AND @ScoreRowCount > 0
                THEN 'ProceedToFacultyIdentityCandidateSummaryGeneration'
            WHEN @LatestRunStatus <> 'Completed'
                THEN 'InspectLatestWorkEvidenceScoringRunFailure'
            ELSE 'ReviewFacultyWithZeroScoredWorksBeforeSummaryGeneration'
        END;
