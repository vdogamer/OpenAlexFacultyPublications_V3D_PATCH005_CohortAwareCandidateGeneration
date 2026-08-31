/*
V3E PATCH017B11 - Five-Faculty Publication Candidate Execution Diagnostics
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestRunId BIGINT;

SELECT TOP (1)
    @LatestRunId = FacultyPublicationCohortExecutionRunId
FROM openalex.FacultyPublicationCohortExecutionRun
WHERE CohortName = @CohortName
ORDER BY FacultyPublicationCohortExecutionRunId DESC;

IF @LatestRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B11 execution run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Latest PATCH017B11 execution run';
SELECT *
FROM openalex.FacultyPublicationCohortExecutionRun
WHERE FacultyPublicationCohortExecutionRunId = @LatestRunId;

PRINT '2) Inserted publication candidates by faculty';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    CandidateCount = COUNT(*),
    DeduplicatedCollisionCandidateCount =
        SUM(CASE WHEN i.RawMatchCountForFacultyWork > 1 THEN 1 ELSE 0 END),
    BaseScore100Count =
        SUM(CASE WHEN i.ProposedMatchScore = 100 THEN 1 ELSE 0 END),
    BaseScore95Count =
        SUM(CASE WHEN i.ProposedMatchScore = 95 THEN 1 ELSE 0 END),
    BaseScore90Count =
        SUM(CASE WHEN i.ProposedMatchScore = 90 THEN 1 ELSE 0 END),
    BaseScore85Count =
        SUM(CASE WHEN i.ProposedMatchScore = 85 THEN 1 ELSE 0 END),
    BaseScore80Count =
        SUM(CASE WHEN i.ProposedMatchScore = 80 THEN 1 ELSE 0 END)
FROM openalex.FacultyPublicationCohortExecutionItem i
WHERE i.FacultyPublicationCohortExecutionRunId = @LatestRunId
  AND i.ExecutionStatus = 'Completed'
GROUP BY i.FacultyId, i.FacultyDisplayName
ORDER BY i.FacultyDisplayName;

PRINT '3) Candidate verification';
SELECT TOP (200)
    i.FacultyId,
    i.FacultyDisplayName,
    i.WorkOpenAlexId,
    i.WorkTitle,
    i.AuthorOpenAlexId,
    i.MatchMethod,
    i.RawMatchCountForFacultyWork,
    i.ProposedMatchScore,
    i.CandidateIdBefore,
    i.CandidateIdAfter,
    c.CandidateSource,
    c.CandidateStatus,
    c.MatchScore,
    CandidateVerified =
        CASE
            WHEN c.FacultyPublicationCandidateId = i.CandidateIdAfter
             AND c.CandidateSource = 'AcceptedAuthorMatch'
             AND c.CandidateStatus = 'PendingReview'
             AND c.MatchScore = i.ProposedMatchScore
                THEN 1 ELSE 0
        END
FROM openalex.FacultyPublicationCohortExecutionItem i
LEFT JOIN openalex.FacultyPublicationCandidate c
    ON c.FacultyPublicationCandidateId = i.CandidateIdAfter
WHERE i.FacultyPublicationCohortExecutionRunId = @LatestRunId
ORDER BY i.FacultyDisplayName, i.WorkOpenAlexId;

PRINT '4) PATCH017B11 readiness summary';
DECLARE @RunStatus VARCHAR(30);
DECLARE @SelectedCandidateCount INT;
DECLARE @CandidateRowsInserted INT;
DECLARE @ValidationFailureCount INT;
DECLARE @PublicationCandidateRowsBefore INT;
DECLARE @PublicationCandidateRowsAfter INT;
DECLARE @PublicationDecisionRowsBefore INT;
DECLARE @PublicationDecisionRowsAfter INT;

SELECT
    @RunStatus = RunStatus,
    @SelectedCandidateCount = SelectedCandidateCount,
    @CandidateRowsInserted = CandidateRowsInserted,
    @ValidationFailureCount = ValidationFailureCount,
    @PublicationCandidateRowsBefore = PublicationCandidateRowsBefore,
    @PublicationCandidateRowsAfter = PublicationCandidateRowsAfter,
    @PublicationDecisionRowsBefore = PublicationDecisionRowsBefore,
    @PublicationDecisionRowsAfter = PublicationDecisionRowsAfter
FROM openalex.FacultyPublicationCohortExecutionRun
WHERE FacultyPublicationCohortExecutionRunId = @LatestRunId;

DECLARE @VerifiedCandidateCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyPublicationCohortExecutionItem i
    INNER JOIN openalex.FacultyPublicationCandidate c
        ON c.FacultyPublicationCandidateId = i.CandidateIdAfter
       AND c.CandidateSource = 'AcceptedAuthorMatch'
       AND c.CandidateStatus = 'PendingReview'
       AND c.MatchScore = i.ProposedMatchScore
    WHERE i.FacultyPublicationCohortExecutionRunId = @LatestRunId
      AND i.ExecutionStatus = 'Completed'
);

DECLARE @FacultyWithCandidates INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.FacultyPublicationCohortExecutionItem
    WHERE FacultyPublicationCohortExecutionRunId = @LatestRunId
      AND ExecutionStatus = 'Completed'
);

SELECT
    LatestExecutionRunId = @LatestRunId,
    RunStatus = @RunStatus,
    SelectedCandidateCount = @SelectedCandidateCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    ValidationFailureCount = @ValidationFailureCount,
    VerifiedCandidateCount = @VerifiedCandidateCount,
    FacultyWithCandidates = @FacultyWithCandidates,
    PublicationCandidateRowsBefore = @PublicationCandidateRowsBefore,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsBefore = @PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    SafetyStatus =
        CASE
            WHEN @PublicationCandidateRowsAfter =
                    @PublicationCandidateRowsBefore + ISNULL(@CandidateRowsInserted, 0)
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'ControlledExecution_CandidateInsertOnly_NoPublicationDecisionChange'
            ELSE 'InvestigateCandidateOrDecisionCountChange'
        END,
    NextAction =
        CASE
            WHEN @RunStatus = 'Completed'
             AND @ValidationFailureCount = 0
             AND @VerifiedCandidateCount = @SelectedCandidateCount
             AND @FacultyWithCandidates = 5
             AND @PublicationCandidateRowsAfter =
                    @PublicationCandidateRowsBefore + ISNULL(@CandidateRowsInserted, 0)
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'OpenPublicationReviewWorkflowForFiveFacultyCohort'
            WHEN @RunStatus <> 'Completed'
                THEN 'InspectPATCH017B11ExecutionFailure'
            ELSE 'ReviewPATCH017B11VerificationMismatch'
        END;
