/*
V3E PATCH017B11A - Five-Faculty Publication Evidence Enhancement Diagnostics
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestRunId BIGINT;

SELECT TOP (1)
    @LatestRunId = FacultyPublicationCohortEvidenceEnhancementRunId
FROM openalex.FacultyPublicationCohortEvidenceEnhancementRun
WHERE CohortName = @CohortName
ORDER BY FacultyPublicationCohortEvidenceEnhancementRunId DESC;

IF @LatestRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B11A evidence enhancement run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Latest PATCH017B11A run';
SELECT *
FROM openalex.FacultyPublicationCohortEvidenceEnhancementRun
WHERE FacultyPublicationCohortEvidenceEnhancementRunId = @LatestRunId;

DECLARE @SourceCandidateIngestionRunId BIGINT;
DECLARE @ExpectedCandidateCount INT;
DECLARE @RunStatus VARCHAR(30);

SELECT
    @SourceCandidateIngestionRunId = SourceCandidateIngestionRunId,
    @ExpectedCandidateCount = ExpectedCandidateCount,
    @RunStatus = RunStatus
FROM openalex.FacultyPublicationCohortEvidenceEnhancementRun
WHERE FacultyPublicationCohortEvidenceEnhancementRunId = @LatestRunId;

PRINT '2) Evidence distribution by faculty';
SELECT
    pc.FacultyId,
    f.DisplayName,
    CandidateCount = COUNT(*),
    RowsWithInstitutionEvidence =
        SUM(CASE
            WHEN pc.EvidenceSummary LIKE '%AffiliatedInstitutionMatchCount=%'
             AND pc.EvidenceSummary NOT LIKE '%AffiliatedInstitutionMatchCount=0%'
                THEN 1 ELSE 0 END),
    RowsWithCoauthorEvidence =
        SUM(CASE
            WHEN pc.EvidenceSummary LIKE '%KnownCoauthorOverlapCount=%'
             AND pc.EvidenceSummary NOT LIKE '%KnownCoauthorOverlapCount=0%'
                THEN 1 ELSE 0 END),
    RowsWithEvidenceBoost =
        SUM(CASE
            WHEN pc.EvidenceSummary LIKE '%EvidenceBoost=%'
             AND pc.EvidenceSummary NOT LIKE '%EvidenceBoost=0%'
                THEN 1 ELSE 0 END),
    Score100Count = SUM(CASE WHEN pc.MatchScore = 100 THEN 1 ELSE 0 END),
    Score95Count = SUM(CASE WHEN pc.MatchScore = 95 THEN 1 ELSE 0 END),
    Score90Count = SUM(CASE WHEN pc.MatchScore = 90 THEN 1 ELSE 0 END),
    Score85Count = SUM(CASE WHEN pc.MatchScore = 85 THEN 1 ELSE 0 END),
    Score80Count = SUM(CASE WHEN pc.MatchScore = 80 THEN 1 ELSE 0 END)
FROM openalex.FacultyPublicationCandidate pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.IngestionRunId = @SourceCandidateIngestionRunId
  AND EXISTS
  (
      SELECT 1
      FROM openalex.PilotFacultyCohort cohort
      WHERE cohort.FacultyId = pc.FacultyId
        AND cohort.CohortName = @CohortName
        AND cohort.IsEnabled = 1
  )
GROUP BY pc.FacultyId, f.DisplayName
ORDER BY f.DisplayName;

PRINT '3) Sample enhanced candidates';
SELECT TOP (30)
    pc.FacultyPublicationCandidateId,
    f.DisplayName AS FacultyDisplayName,
    pc.WorkTitle,
    pc.MatchScore,
    pc.EvidenceSummary
FROM openalex.FacultyPublicationCandidate pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.IngestionRunId = @SourceCandidateIngestionRunId
  AND EXISTS
  (
      SELECT 1
      FROM openalex.PilotFacultyCohort cohort
      WHERE cohort.FacultyId = pc.FacultyId
        AND cohort.CohortName = @CohortName
        AND cohort.IsEnabled = 1
  )
ORDER BY pc.MatchScore DESC, f.DisplayName, pc.FacultyPublicationCandidateId;

PRINT '4) PATCH017B11A readiness summary';
DECLARE @CandidateRowsTotalBefore INT;
DECLARE @CandidateRowsTotalAfter INT;
DECLARE @PublicationDecisionRowsBefore INT;
DECLARE @PublicationDecisionRowsAfter INT;
DECLARE @SourceCandidateRowsAfter INT;
DECLARE @SourceCandidatesWithEvidenceAfter INT;

SELECT
    @CandidateRowsTotalBefore = CandidateRowsTotalBefore,
    @CandidateRowsTotalAfter = CandidateRowsTotalAfter,
    @PublicationDecisionRowsBefore = PublicationDecisionRowsBefore,
    @PublicationDecisionRowsAfter = PublicationDecisionRowsAfter,
    @SourceCandidateRowsAfter = SourceCandidateRowsAfter,
    @SourceCandidatesWithEvidenceAfter = SourceCandidatesWithEvidenceAfter
FROM openalex.FacultyPublicationCohortEvidenceEnhancementRun
WHERE FacultyPublicationCohortEvidenceEnhancementRunId = @LatestRunId;

SELECT
    LatestEvidenceEnhancementRunId = @LatestRunId,
    RunStatus = @RunStatus,
    ExpectedCandidateCount = @ExpectedCandidateCount,
    SourceCandidateRowsAfter = @SourceCandidateRowsAfter,
    SourceCandidatesWithEvidenceAfter = @SourceCandidatesWithEvidenceAfter,
    CandidateRowsTotalBefore = @CandidateRowsTotalBefore,
    CandidateRowsTotalAfter = @CandidateRowsTotalAfter,
    PublicationDecisionRowsBefore = @PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    SafetyStatus =
        CASE
            WHEN @CandidateRowsTotalBefore = @CandidateRowsTotalAfter
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'EvidenceEnhanced_NoCandidateOrDecisionRowCountChange'
            ELSE 'InvestigateCandidateOrDecisionRowCountChange'
        END,
    NextAction =
        CASE
            WHEN @RunStatus = 'Completed'
             AND @SourceCandidateRowsAfter = @ExpectedCandidateCount
             AND @SourceCandidatesWithEvidenceAfter = @ExpectedCandidateCount
             AND @CandidateRowsTotalBefore = @CandidateRowsTotalAfter
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'OpenPublicationReviewWorkflowForFiveFacultyCohort'
            WHEN @RunStatus <> 'Completed'
                THEN 'InspectPATCH017B11AEvidenceEnhancementFailure'
            ELSE 'ReviewPATCH017B11AEvidenceVerificationMismatch'
        END;
