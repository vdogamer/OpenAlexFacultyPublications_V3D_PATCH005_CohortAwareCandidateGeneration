/*
V3E PATCH017B2 - Controlled Faculty-Publication Candidate Generation Preview Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

DECLARE @PreviewRunId BIGINT = NULL;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U') IS NULL
BEGIN
    SELECT 'Missing PATCH017B2 preview tables. Run sql/374_V3E_PATCH017B2_FacultyPublicationCandidatePreview_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT @PreviewRunId = MAX(FacultyPublicationCandidatePreviewRunId)
    FROM openalex.FacultyPublicationCandidatePreviewRun;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT 'No PATCH017B2 candidate preview runs were found.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Selected candidate preview run';
SELECT
    FacultyPublicationCandidatePreviewRunId,
    SourceWorkBackfillRunId,
    FacultyId,
    MaxWorks,
    SelectedWorkCount,
    ReadyToGenerateCount,
    ExistingCandidateCount,
    NoAcceptedAuthorDecisionCount,
    MultipleAcceptedAuthorDecisionCount,
    AcceptedAuthorNotOnWorkCount,
    CandidateRowsBefore,
    CandidateRowsAfter,
    DecisionRowsBefore,
    DecisionRowsAfter,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    SummaryText,
    ErrorText,
    StartedAtUtc,
    CompletedAtUtc
FROM openalex.FacultyPublicationCandidatePreviewRun
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId;

PRINT '2) Preview status counts';
SELECT
    PreviewStatus,
    PreviewItemCount = COUNT(*)
FROM openalex.FacultyPublicationCandidatePreviewItem
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
GROUP BY PreviewStatus
ORDER BY PreviewStatus;

PRINT '3) Detailed proposed faculty-publication candidates';
SELECT
    FacultyPublicationCandidatePreviewItemId,
    SourceBackfillItemId,
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    PublicationYear,
    WorkDoi,
    AcceptedDecisionCount,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkAuthorshipId,
    AcceptedAuthorAppearsOnWork = CASE WHEN WorkAuthorshipId IS NULL THEN 0 ELSE 1 END,
    OrdinalPosition,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    ExistingCandidateId,
    ProposedCandidateSource,
    ProposedCandidateStatus,
    ProposedMatchScore,
    PreviewStatus,
    EvidenceSummary
FROM openalex.FacultyPublicationCandidatePreviewItem
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;

PRINT '4) Accepted-author identity and authorship verification';
SELECT
    FacultyId,
    FacultyDisplayName,
    AcceptedDecisionCount,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkOpenAlexId,
    WorkTitle,
    WorkAuthorshipId,
    AcceptedAuthorAppearsOnWork = CASE WHEN WorkAuthorshipId IS NULL THEN 0 ELSE 1 END,
    PreviewStatus
FROM openalex.FacultyPublicationCandidatePreviewItem
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
ORDER BY WorkTitle;

PRINT '5) Proposed score breakdown';
SELECT
    FacultyId,
    FacultyDisplayName,
    WorkOpenAlexId,
    WorkTitle,
    BaseAcceptedAuthorScore = CASE WHEN WorkAuthorshipId IS NULL THEN 0 ELSE 80 END,
    CorrespondingAuthorPoints = CASE WHEN WorkAuthorshipId IS NOT NULL AND IsCorresponding = 1 THEN 5 ELSE 0 END,
    MatchingInstitutionPoints = CASE WHEN WorkAuthorshipId IS NOT NULL AND MatchingInstitutionCount > 0 THEN 10 ELSE 0 END,
    AnyAuthorshipInstitutionPoints = CASE WHEN WorkAuthorshipId IS NOT NULL AND AuthorshipInstitutionCount > 0 THEN 5 ELSE 0 END,
    ProposedMatchScore,
    PreviewStatus
FROM openalex.FacultyPublicationCandidatePreviewItem
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
ORDER BY ProposedMatchScore DESC, WorkTitle;

PRINT '6) Candidate and decision safety verification';
SELECT
    CandidateRowsBefore,
    CandidateRowsAfter,
    DecisionRowsBefore,
    DecisionRowsAfter,
    SafetyStatus = CASE
        WHEN RunStatus <> 'Completed' OR CandidateRowsAfter IS NULL OR DecisionRowsAfter IS NULL
            THEN 'NotEvaluated_RunIncomplete'
        WHEN CandidateRowsBefore = CandidateRowsAfter
         AND DecisionRowsBefore = DecisionRowsAfter
            THEN 'PreviewOnly_NoCandidateOrDecisionChange'
        ELSE 'UnexpectedCandidateOrDecisionCountChange'
    END
FROM openalex.FacultyPublicationCandidatePreviewRun
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId;

PRINT '7) Current database cross-check and next-action classification';
SELECT
    p.FacultyId,
    p.FacultyDisplayName,
    p.WorkId,
    p.WorkOpenAlexId,
    p.WorkTitle,
    p.PreviewStatus,
    p.ProposedMatchScore,
    CurrentCandidateId = pc.FacultyPublicationCandidateId,
    CurrentCandidateStatus = pc.CandidateStatus,
    NextAction = CASE
        WHEN pc.FacultyPublicationCandidateId IS NOT NULL THEN 'NoInsert_CandidateAlreadyExists'
        WHEN p.PreviewStatus = 'ReadyToGenerate' THEN 'EligibleForControlledCandidateInsert'
        WHEN p.PreviewStatus = 'NoAcceptedAuthorDecision' THEN 'ResolveAcceptedAuthorIdentityFirst'
        WHEN p.PreviewStatus = 'MultipleAcceptedAuthorDecisions' THEN 'ResolveMultipleAcceptedAuthorDecisionsFirst'
        WHEN p.PreviewStatus = 'AcceptedAuthorNotOnWork' THEN 'DoNotGenerate_ReviewIdentityOrAuthorship'
        ELSE 'ManualReviewRequired'
    END
FROM openalex.FacultyPublicationCandidatePreviewItem p
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON pc.FacultyId = p.FacultyId
   AND pc.WorkId = p.WorkId
   AND pc.CandidateSource = 'AcceptedAuthorMatch'
WHERE p.FacultyPublicationCandidatePreviewRunId = @PreviewRunId
ORDER BY p.WorkTitle;
