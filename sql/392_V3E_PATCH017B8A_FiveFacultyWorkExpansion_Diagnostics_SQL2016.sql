/*
V3E PATCH017B8A - Five-Faculty Bounded Work Expansion Diagnostics
SQL Server 2016 compatible.

Purpose
-------
Verify the most recent FacultyIdentityCandidateWorkExpansion run for the
V3E_PATCH017B8_FIVE_FACULTY cohort and confirm that the expansion stage did not
create author decisions, publication candidates, or publication decisions.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestRunId BIGINT;

SELECT TOP (1)
    @LatestRunId = ir.IngestionRunId
FROM openalex.IngestionRun ir
WHERE ir.InstitutionFilter LIKE N'FacultyIdentityCandidateWorkExpansion%'
  AND ir.InstitutionFilter LIKE N'%PilotCohortName=' + @CohortName + N'%'
ORDER BY ir.IngestionRunId DESC;

IF @LatestRunId IS NULL
BEGIN
    RAISERROR('No FacultyIdentityCandidateWorkExpansion run was found for the PATCH017B8 cohort.', 16, 1);
    RETURN;
END;

PRINT '1) Latest bounded work-expansion run';
SELECT
    ir.IngestionRunId,
    ir.RunMode,
    ir.Status,
    ir.InstitutionFilter,
    ir.AuthorsProcessed,
    ir.WorksProcessed,
    ir.SummaryText,
    ir.StartedAtUtc,
    ir.FinishedAtUtc,
    ir.ErrorCount
FROM openalex.IngestionRun ir
WHERE ir.IngestionRunId = @LatestRunId;

PRINT '2) Expanded work counts by cohort faculty';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    SelectedDistinctAuthorCount = COUNT(DISTINCT w.CandidateAuthorOpenAlexId),
    ExpandedDistinctWorkCount = COUNT(DISTINCT w.WorkOpenAlexId),
    ExpandedWorkRowCount = COUNT(w.FacultyIdentityCandidateWorkId)
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
LEFT JOIN openalex.FacultyIdentityCandidateWork w
    ON w.FacultyId = pc.FacultyId
   AND w.IngestionRunId = @LatestRunId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
GROUP BY pc.PilotOrder, f.FacultyId, f.DisplayName
ORDER BY pc.PilotOrder;

PRINT '3) Expanded work counts by selected author';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    w.CandidateAuthorOpenAlexId,
    ExpandedWorkCount = COUNT(*),
    DistinctWorkCount = COUNT(DISTINCT w.WorkOpenAlexId),
    MinPublicationYear = MIN(w.PublicationYear),
    MaxPublicationYear = MAX(w.PublicationYear)
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
INNER JOIN openalex.FacultyIdentityCandidateWork w
    ON w.FacultyId = pc.FacultyId
   AND w.IngestionRunId = @LatestRunId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
GROUP BY pc.PilotOrder, f.FacultyId, f.DisplayName, w.CandidateAuthorOpenAlexId
ORDER BY pc.PilotOrder, ExpandedWorkCount DESC, w.CandidateAuthorOpenAlexId;

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
    SafetyStatus = 'WorkExpansionOnly_NoDecisionsOrPublicationCandidatesExpected';

PRINT '5) Expansion readiness summary';
DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithExpandedWorks INT =
(
    SELECT COUNT(DISTINCT w.FacultyId)
    FROM openalex.FacultyIdentityCandidateWork w
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = w.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE w.IngestionRunId = @LatestRunId
);

DECLARE @ExpandedAuthorCount INT =
(
    SELECT COUNT(*)
    FROM
    (
        SELECT DISTINCT w.FacultyId, w.CandidateAuthorOpenAlexId
        FROM openalex.FacultyIdentityCandidateWork w
        INNER JOIN openalex.PilotFacultyCohort pc
            ON pc.FacultyId = w.FacultyId
           AND pc.CohortName = @CohortName
           AND pc.IsEnabled = 1
        WHERE w.IngestionRunId = @LatestRunId
    ) x
);

DECLARE @ExpandedWorkRowCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityCandidateWork w
    INNER JOIN openalex.PilotFacultyCohort pc
        ON pc.FacultyId = w.FacultyId
       AND pc.CohortName = @CohortName
       AND pc.IsEnabled = 1
    WHERE w.IngestionRunId = @LatestRunId
);

DECLARE @LatestRunStatus VARCHAR(50) =
(
    SELECT Status FROM openalex.IngestionRun WHERE IngestionRunId = @LatestRunId
);

SELECT
    LatestRunId = @LatestRunId,
    LatestRunStatus = @LatestRunStatus,
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithExpandedWorks = @FacultyWithExpandedWorks,
    FacultyWithZeroExpandedWorks = @EnabledFacultyCount - @FacultyWithExpandedWorks,
    ExpandedDistinctAuthorCount = @ExpandedAuthorCount,
    ExpandedWorkRowCount = @ExpandedWorkRowCount,
    NextAction =
        CASE
            WHEN @LatestRunStatus = 'Completed'
             AND @FacultyWithExpandedWorks = @EnabledFacultyCount
             AND @ExpandedWorkRowCount > 0
                THEN 'ProceedToFacultyIdentityWorkEvidenceScoring'
            WHEN @LatestRunStatus <> 'Completed'
                THEN 'InspectLatestWorkExpansionRunFailure'
            ELSE 'ReviewFacultyWithZeroExpandedWorksBeforeEvidenceScoring'
        END;
