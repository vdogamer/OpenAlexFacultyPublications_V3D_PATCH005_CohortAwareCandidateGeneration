/*
V3E PATCH017B6 - Split-Identity-Aware Faculty-Publication Candidate Preview Diagnostics
SQL Server 2016-safe and read-only.

Purpose:
  Verify that a faculty member may have multiple current accepted OpenAlex identities
  while each publication is evaluated against the accepted identity that appears on
  that specific work.
*/

SET NOCOUNT ON;

DECLARE @PreviewRunId BIGINT = NULL;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewItem', N'U') IS NULL
BEGIN
    RAISERROR('Missing PATCH017B2/B6 preview tables. Run sql/374_V3E_PATCH017B2_FacultyPublicationCandidatePreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT @PreviewRunId = MAX(FacultyPublicationCandidatePreviewRunId)
    FROM openalex.FacultyPublicationCandidatePreviewRun
    WHERE ApplicationVersion = '3E-PATCH017B6-SplitIdentityAwarePublicationPreview';
END;

IF @PreviewRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B6 split-identity-aware preview run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Selected PATCH017B6 preview run';
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

PRINT '3) Split-identity-aware work mapping';
SELECT
    p.FacultyPublicationCandidatePreviewItemId,
    p.FacultyId,
    p.FacultyDisplayName,
    AcceptedFacultyIdentityCount = p.AcceptedDecisionCount,
    p.FacultyAuthorMatchDecisionId,
    SelectedAcceptedAuthorOpenAlexId = p.AuthorOpenAlexId,
    SelectedAcceptedAuthorDisplayName = p.AuthorDisplayName,
    SelectedAcceptedAuthorOrcid = p.AuthorOrcid,
    p.WorkId,
    p.WorkOpenAlexId,
    p.WorkTitle,
    p.PublicationYear,
    p.WorkAuthorshipId,
    SelectedAcceptedAuthorAppearsOnWork = CASE WHEN p.WorkAuthorshipId IS NULL THEN 0 ELSE 1 END,
    p.OrdinalPosition,
    p.IsCorresponding,
    p.AuthorshipInstitutionCount,
    p.MatchingInstitutionCount,
    p.ProposedMatchScore,
    p.PreviewStatus,
    SplitIdentityClassification = CASE
        WHEN p.PreviewStatus = 'ReadyToGenerate'
         AND p.AcceptedDecisionCount > 1
         AND p.WorkAuthorshipId IS NOT NULL
            THEN 'Ready_MultipleAcceptedIdentities_OneSelectedOnWork'
        WHEN p.PreviewStatus = 'ReadyToGenerate'
         AND p.WorkAuthorshipId IS NOT NULL
            THEN 'Ready_AcceptedIdentitySelectedOnWork'
        WHEN p.PreviewStatus = 'AlreadyCandidateExists'
            THEN 'AlreadyCandidateExists'
        WHEN p.PreviewStatus = 'NoAcceptedAuthorDecision'
            THEN 'Blocked_NoAcceptedIdentity'
        WHEN p.PreviewStatus = 'AcceptedAuthorNotOnWork'
            THEN 'Blocked_NoAcceptedIdentityOnWork'
        ELSE 'ReviewRequired'
    END
FROM openalex.FacultyPublicationCandidatePreviewItem p
WHERE p.FacultyPublicationCandidatePreviewRunId = @PreviewRunId
ORDER BY p.PublicationYear DESC, p.WorkTitle;

PRINT '4) Current accepted faculty-author decisions';
SELECT
    d.FacultyAuthorMatchDecisionId,
    d.FacultyId,
    d.AuthorOpenAlexId,
    d.AuthorDisplayName,
    d.AuthorOrcid,
    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.IsCurrent,
    d.ReviewedBy,
    d.ReviewedAtUtc
FROM openalex.FacultyAuthorMatchDecision d
WHERE d.FacultyId =
(
    SELECT FacultyId
    FROM openalex.FacultyPublicationCandidatePreviewRun
    WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
)
  AND d.DecisionStatus = 'Accepted'
  AND d.IsCurrent = 1
ORDER BY d.FacultyAuthorMatchDecisionId;

PRINT '5) Selected accepted identity -> WorkAuthorship cross-check';
SELECT
    p.FacultyId,
    p.WorkOpenAlexId,
    p.WorkTitle,
    p.AuthorOpenAlexId AS SelectedAcceptedAuthorOpenAlexId,
    p.AuthorOrcid AS SelectedAcceptedAuthorOrcid,
    p.WorkAuthorshipId AS PreviewWorkAuthorshipId,
    CurrentAcceptedDecisionExists = CASE WHEN d.FacultyAuthorMatchDecisionId IS NULL THEN 0 ELSE 1 END,
    CurrentMatchingWorkAuthorshipId = wa.WorkAuthorshipId,
    CurrentAcceptedAuthorAppearsOnWork = CASE WHEN wa.WorkAuthorshipId IS NULL THEN 0 ELSE 1 END,
    p.PreviewStatus
FROM openalex.FacultyPublicationCandidatePreviewItem p
LEFT JOIN openalex.FacultyAuthorMatchDecision d
    ON d.FacultyAuthorMatchDecisionId = p.FacultyAuthorMatchDecisionId
   AND d.FacultyId = p.FacultyId
   AND d.DecisionStatus = 'Accepted'
   AND d.IsCurrent = 1
OUTER APPLY
(
    SELECT TOP (1)
        wax.WorkAuthorshipId
    FROM openalex.WorkAuthorship wax
    WHERE wax.WorkId = p.WorkId
      AND
      (
          wax.AuthorOpenAlexId = p.AuthorOpenAlexId
          OR
          (
              p.AuthorOrcid IS NOT NULL
              AND wax.AuthorOrcid IS NOT NULL
              AND wax.AuthorOrcid = p.AuthorOrcid
          )
      )
    ORDER BY wax.WorkAuthorshipId
) wa
WHERE p.FacultyPublicationCandidatePreviewRunId = @PreviewRunId
ORDER BY p.WorkTitle;

PRINT '6) Safety verification';
SELECT
    CandidateRowsBefore,
    CandidateRowsAfter,
    DecisionRowsBefore,
    DecisionRowsAfter,
    SafetyStatus = CASE
        WHEN RunStatus <> 'Completed'
            THEN 'NotEvaluated_RunIncomplete'
        WHEN CandidateRowsAfter IS NULL OR DecisionRowsAfter IS NULL
            THEN 'NotEvaluated_MissingAfterCounts'
        WHEN CandidateRowsBefore = CandidateRowsAfter
         AND DecisionRowsBefore = DecisionRowsAfter
            THEN 'PreviewOnly_NoPublicationCandidateOrDecisionChange'
        ELSE 'UnexpectedPublicationCandidateOrDecisionCountChange'
    END
FROM openalex.FacultyPublicationCandidatePreviewRun
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId;

PRINT '7) B6 readiness summary';
SELECT
    PreviewRunId = @PreviewRunId,
    SelectedWorkCount = COUNT(*),
    ReadyToGenerateCount = SUM(CASE WHEN p.PreviewStatus = 'ReadyToGenerate' THEN 1 ELSE 0 END),
    ReadyWithMultipleAcceptedFacultyIdentities = SUM(CASE
        WHEN p.PreviewStatus = 'ReadyToGenerate'
         AND p.AcceptedDecisionCount > 1
         AND p.WorkAuthorshipId IS NOT NULL THEN 1 ELSE 0 END),
    MissingAcceptedIdentityCount = SUM(CASE WHEN p.PreviewStatus = 'NoAcceptedAuthorDecision' THEN 1 ELSE 0 END),
    AcceptedAuthorNotOnWorkCount = SUM(CASE WHEN p.PreviewStatus = 'AcceptedAuthorNotOnWork' THEN 1 ELSE 0 END),
    NextAction = CASE
        WHEN COUNT(*) > 0
         AND SUM(CASE WHEN p.PreviewStatus = 'ReadyToGenerate' THEN 1 ELSE 0 END) = COUNT(*)
            THEN 'ProceedToControlledFacultyPublicationCandidateExecution'
        ELSE 'ResolveRemainingPreviewBlocksBeforeCandidateExecution'
    END
FROM openalex.FacultyPublicationCandidatePreviewItem p
WHERE p.FacultyPublicationCandidatePreviewRunId = @PreviewRunId;
