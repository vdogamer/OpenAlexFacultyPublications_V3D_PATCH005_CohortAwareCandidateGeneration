/*
V3E PATCH017B7 - Controlled Faculty-Publication Candidate Execution Diagnostics
SQL Server 2016-safe and read-only.
*/

SET NOCOUNT ON;

DECLARE @ExecutionRunId BIGINT = NULL;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionRun', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionItem', N'U') IS NULL
BEGIN
    RAISERROR('Missing PATCH017B7 execution tables. Run sql/383_V3E_PATCH017B7_FacultyPublicationCandidateExecution_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF @ExecutionRunId IS NULL
BEGIN
    SELECT @ExecutionRunId = MAX(FacultyPublicationCandidateExecutionRunId)
    FROM openalex.FacultyPublicationCandidateExecutionRun
    WHERE ApplicationVersion = '3E-PATCH017B7-PubCandidateExec';
END;

IF @ExecutionRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B7 execution run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Selected PATCH017B7 execution run';
SELECT
    FacultyPublicationCandidateExecutionRunId,
    SourceFacultyPublicationCandidatePreviewRunId,
    FacultyId,
    MaxCandidates,
    ConfirmedExecution,
    IngestionRunId,
    SelectedCandidateCount,
    CandidateRowsInserted,
    CandidatesAlreadyExist,
    ValidationFailureCount,
    PublicationCandidateRowsBefore,
    PublicationCandidateRowsAfter,
    PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    SummaryText,
    ErrorText,
    StartedAtUtc,
    CompletedAtUtc
FROM openalex.FacultyPublicationCandidateExecutionRun
WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId;

PRINT '2) Execution action counts';
SELECT
    CandidateExecutionAction,
    ExecutionStatus,
    ItemCount = COUNT(*)
FROM openalex.FacultyPublicationCandidateExecutionItem
WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId
GROUP BY CandidateExecutionAction, ExecutionStatus
ORDER BY CandidateExecutionAction, ExecutionStatus;

PRINT '3) Detailed controlled candidate execution items';
SELECT
    i.FacultyPublicationCandidateExecutionItemId,
    i.SourceFacultyPublicationCandidatePreviewItemId,
    i.FacultyId,
    i.FacultyDisplayName,
    i.WorkId,
    i.WorkOpenAlexId,
    i.WorkTitle,
    i.FacultyAuthorMatchDecisionId,
    i.SelectedAuthorOpenAlexId,
    i.WorkAuthorshipId,
    i.ProposedMatchScore,
    i.CandidateIdBefore,
    i.CandidateIdAfter,
    i.CandidateExecutionAction,
    i.ExecutionStatus,
    i.WarningText
FROM openalex.FacultyPublicationCandidateExecutionItem i
WHERE i.FacultyPublicationCandidateExecutionRunId = @ExecutionRunId
ORDER BY i.FacultyPublicationCandidateExecutionItemId;

PRINT '4) Created/reused FacultyPublicationCandidate verification';
SELECT
    i.FacultyId,
    i.WorkOpenAlexId,
    i.WorkTitle,
    i.CandidateIdAfter,
    pc.FacultyPublicationCandidateId,
    pc.FacultyAuthorMatchDecisionId,
    pc.WorkId,
    pc.WorkOpenAlexId,
    pc.CandidateSource,
    pc.CandidateStatus,
    pc.MatchScore,
    pc.IngestionRunId,
    CandidateVerified = CASE
        WHEN pc.FacultyPublicationCandidateId = i.CandidateIdAfter
         AND pc.FacultyId = i.FacultyId
         AND pc.WorkId = i.WorkId
         AND pc.WorkOpenAlexId = i.WorkOpenAlexId
         AND pc.FacultyAuthorMatchDecisionId = i.FacultyAuthorMatchDecisionId
         AND pc.CandidateSource = 'AcceptedAuthorMatch'
            THEN 1 ELSE 0 END,
    PendingReviewOnFreshInsert = CASE
        WHEN i.CandidateExecutionAction <> 'InsertedPendingReview' THEN NULL
        WHEN pc.CandidateStatus = 'PendingReview' THEN 1 ELSE 0 END
FROM openalex.FacultyPublicationCandidateExecutionItem i
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON pc.FacultyPublicationCandidateId = i.CandidateIdAfter
WHERE i.FacultyPublicationCandidateExecutionRunId = @ExecutionRunId
ORDER BY i.WorkTitle;

PRINT '5) Frozen B6 preview -> B7 candidate lineage';
SELECT
    p.FacultyPublicationCandidatePreviewRunId,
    p.FacultyPublicationCandidatePreviewItemId,
    p.PreviewStatus,
    p.FacultyId,
    p.AuthorOpenAlexId AS B6SelectedAcceptedAuthorOpenAlexId,
    p.WorkAuthorshipId AS B6WorkAuthorshipId,
    p.WorkOpenAlexId,
    p.ProposedMatchScore AS B6ProposedMatchScore,
    i.FacultyPublicationCandidateExecutionRunId,
    i.CandidateIdAfter,
    pc.MatchScore AS ExecutedCandidateMatchScore,
    LineageStatus = CASE
        WHEN p.PreviewStatus = 'ReadyToGenerate'
         AND i.CandidateIdAfter IS NOT NULL
         AND pc.FacultyAuthorMatchDecisionId = p.FacultyAuthorMatchDecisionId
         AND pc.WorkId = p.WorkId
         AND pc.MatchScore = p.ProposedMatchScore
            THEN 'B6ReadyToGenerate_ExecutedConsistently'
        ELSE 'ReviewLineageMismatch'
    END
FROM openalex.FacultyPublicationCandidateExecutionItem i
JOIN openalex.FacultyPublicationCandidatePreviewItem p
    ON p.FacultyPublicationCandidatePreviewItemId = i.SourceFacultyPublicationCandidatePreviewItemId
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON pc.FacultyPublicationCandidateId = i.CandidateIdAfter
WHERE i.FacultyPublicationCandidateExecutionRunId = @ExecutionRunId
ORDER BY p.WorkTitle;

PRINT '6) Candidate insert / publication decision safety verification';
SELECT
    PublicationCandidateRowsBefore,
    PublicationCandidateRowsAfter,
    CandidateRowsInserted,
    ExpectedCandidateRowsAfter = PublicationCandidateRowsBefore + CandidateRowsInserted,
    PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter,
    SafetyStatus = CASE
        WHEN RunStatus <> 'Completed'
            THEN 'NotEvaluated_RunIncomplete'
        WHEN PublicationCandidateRowsAfter IS NULL OR PublicationDecisionRowsAfter IS NULL
            THEN 'NotEvaluated_MissingAfterCounts'
        WHEN PublicationCandidateRowsAfter = PublicationCandidateRowsBefore + CandidateRowsInserted
         AND PublicationDecisionRowsAfter = PublicationDecisionRowsBefore
            THEN 'ControlledExecution_CandidateInsertOnly_NoPublicationDecisionChange'
        ELSE 'UnexpectedCandidateOrPublicationDecisionCountChange'
    END
FROM openalex.FacultyPublicationCandidateExecutionRun
WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId;

PRINT '7) Current publication-review readiness';
SELECT
    ExecutionRunId = @ExecutionRunId,
    SelectedCandidateCount = COUNT(*),
    VerifiedCandidateCount = SUM(CASE
        WHEN pc.FacultyPublicationCandidateId = i.CandidateIdAfter
         AND pc.CandidateSource = 'AcceptedAuthorMatch'
            THEN 1 ELSE 0 END),
    PendingReviewCount = SUM(CASE WHEN pc.CandidateStatus = 'PendingReview' THEN 1 ELSE 0 END),
    CurrentPublicationDecisionCount = SUM(CASE WHEN d.FacultyPublicationDecisionId IS NOT NULL THEN 1 ELSE 0 END),
    NextAction = CASE
        WHEN COUNT(*) > 0
         AND SUM(CASE
             WHEN pc.FacultyPublicationCandidateId = i.CandidateIdAfter
              AND pc.CandidateSource = 'AcceptedAuthorMatch'
             THEN 1 ELSE 0 END) = COUNT(*)
            THEN 'OpenPublicationReviewWorkflow'
        ELSE 'ResolvePATCH017B7ExecutionOrVerificationIssue'
    END
FROM openalex.FacultyPublicationCandidateExecutionItem i
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON pc.FacultyPublicationCandidateId = i.CandidateIdAfter
OUTER APPLY
(
    SELECT TOP (1)
        pd.FacultyPublicationDecisionId
    FROM openalex.FacultyPublicationDecision pd
    WHERE pd.FacultyId = i.FacultyId
      AND pd.WorkId = i.WorkId
      AND pd.IsCurrent = 1
    ORDER BY pd.FacultyPublicationDecisionId DESC
) d
WHERE i.FacultyPublicationCandidateExecutionRunId = @ExecutionRunId;
