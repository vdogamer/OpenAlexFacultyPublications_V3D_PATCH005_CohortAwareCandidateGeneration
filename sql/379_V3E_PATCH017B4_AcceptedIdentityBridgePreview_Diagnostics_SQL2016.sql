/*
V3E PATCH017B4 - Controlled Accepted-Identity Bridge Preview Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewItem', N'U') IS NULL
BEGIN
    RAISERROR('PATCH017B4 preview tables are missing. Run sql/378_V3E_PATCH017B4_AcceptedIdentityBridgePreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

DECLARE @PreviewRunId BIGINT;

SELECT @PreviewRunId = MAX(AcceptedIdentityBridgePreviewRunId)
FROM openalex.AcceptedIdentityBridgePreviewRun;

IF @PreviewRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B4 preview run exists.', 16, 1);
    RETURN;
END;

PRINT '1) Selected accepted-identity bridge preview run';
SELECT
    AcceptedIdentityBridgePreviewRunId,
    SourceIdentityResolutionPreviewRunId,
    FacultyId,
    MaxIdentities,
    SelectedAcceptedIdentityCount,
    ReadyCandidateInsertCount,
    ReadyCandidateUpdateCount,
    ReadyDecisionInsertCount,
    AlreadyAcceptedDecisionCount,
    MissingIdentitySummaryCount,
    MissingLocalAuthorCount,
    AuthorCandidateRowsBefore,
    AuthorCandidateRowsAfter,
    AuthorDecisionRowsBefore,
    AuthorDecisionRowsAfter,
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
FROM openalex.AcceptedIdentityBridgePreviewRun
WHERE AcceptedIdentityBridgePreviewRunId = @PreviewRunId;

PRINT '2) Preview status and proposed-action counts';
SELECT
    PreviewStatus,
    ProposedCandidateAction,
    ProposedDecisionAction,
    PreviewItemCount = COUNT(*)
FROM openalex.AcceptedIdentityBridgePreviewItem
WHERE AcceptedIdentityBridgePreviewRunId = @PreviewRunId
GROUP BY PreviewStatus, ProposedCandidateAction, ProposedDecisionAction
ORDER BY PreviewStatus, ProposedCandidateAction, ProposedDecisionAction;

PRINT '3) Detailed accepted identities and proposed bridge effects';
SELECT
    AcceptedIdentityBridgePreviewItemId,
    FacultyId,
    FacultyDisplayName,
    FacultyIdentityCandidateDecisionId,
    FacultyIdentityCandidateSummaryId,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    BestIdentityEvidenceScore,
    LocalAuthorId,
    ExistingBridgeCandidateId,
    ExistingBridgeCandidateStatus,
    ExistingBridgeCandidateMatchScore,
    ExistingAcceptedAuthorDecisionId,
    CurrentAuthorDecisionCount,
    ProposedCandidateAction,
    ProposedDecisionAction,
    PreviewStatus,
    ProposedNextAction,
    EvidenceSummary
FROM openalex.AcceptedIdentityBridgePreviewItem
WHERE AcceptedIdentityBridgePreviewRunId = @PreviewRunId
ORDER BY CandidateDisplayName, CandidateAuthorOpenAlexId;

PRINT '4) Accepted identity decision source verification';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    i.CandidateAuthorOpenAlexId,
    i.CandidateDisplayName,
    IdentityDecisionStatus = d.DecisionStatus,
    IdentityDecisionIsCurrent = d.IsCurrent,
    IdentityReviewer = d.ReviewerName,
    IdentityReviewedAtUtc = d.CreatedAtUtc,
    IdentitySummaryExists = CASE WHEN s.FacultyIdentityCandidateSummaryId IS NULL THEN 0 ELSE 1 END,
    i.PreviewStatus,
    i.ProposedNextAction
FROM openalex.AcceptedIdentityBridgePreviewItem i
JOIN openalex.FacultyIdentityCandidateDecision d
    ON d.FacultyIdentityCandidateDecisionId = i.FacultyIdentityCandidateDecisionId
LEFT JOIN openalex.FacultyIdentityCandidateSummary s
    ON s.FacultyIdentityCandidateSummaryId = i.FacultyIdentityCandidateSummaryId
WHERE i.AcceptedIdentityBridgePreviewRunId = @PreviewRunId
ORDER BY i.CandidateDisplayName, i.CandidateAuthorOpenAlexId;

PRINT '5) Preview-only safety verification';
SELECT
    AuthorCandidateRowsBefore,
    AuthorCandidateRowsAfter,
    AuthorCandidateRowsUnchanged = CASE WHEN AuthorCandidateRowsAfter IS NULL THEN 0 WHEN AuthorCandidateRowsBefore = AuthorCandidateRowsAfter THEN 1 ELSE 0 END,
    AuthorDecisionRowsBefore,
    AuthorDecisionRowsAfter,
    AuthorDecisionRowsUnchanged = CASE WHEN AuthorDecisionRowsAfter IS NULL THEN 0 WHEN AuthorDecisionRowsBefore = AuthorDecisionRowsAfter THEN 1 ELSE 0 END,
    PublicationCandidateRowsBefore,
    PublicationCandidateRowsAfter,
    PublicationCandidateRowsUnchanged = CASE WHEN PublicationCandidateRowsAfter IS NULL THEN 0 WHEN PublicationCandidateRowsBefore = PublicationCandidateRowsAfter THEN 1 ELSE 0 END,
    PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter,
    PublicationDecisionRowsUnchanged = CASE WHEN PublicationDecisionRowsAfter IS NULL THEN 0 WHEN PublicationDecisionRowsBefore = PublicationDecisionRowsAfter THEN 1 ELSE 0 END,
    SafetyStatus = CASE
        WHEN RunStatus <> 'Completed'
          OR AuthorCandidateRowsAfter IS NULL
          OR AuthorDecisionRowsAfter IS NULL
          OR PublicationCandidateRowsAfter IS NULL
          OR PublicationDecisionRowsAfter IS NULL
            THEN 'NotEvaluated_RunIncomplete'
        WHEN AuthorCandidateRowsBefore = AuthorCandidateRowsAfter
         AND AuthorDecisionRowsBefore = AuthorDecisionRowsAfter
         AND PublicationCandidateRowsBefore = PublicationCandidateRowsAfter
         AND PublicationDecisionRowsBefore = PublicationDecisionRowsAfter
            THEN 'PreviewOnly_NoCandidateOrDecisionChange'
        ELSE 'UnexpectedCandidateOrDecisionCountChange'
    END
FROM openalex.AcceptedIdentityBridgePreviewRun
WHERE AcceptedIdentityBridgePreviewRunId = @PreviewRunId;

PRINT '6) Current core bridge-state cross-check';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    i.CandidateAuthorOpenAlexId,
    i.CandidateDisplayName,
    CurrentBridgeCandidateId = c.FacultyAuthorMatchCandidateId,
    CurrentBridgeCandidateStatus = c.CandidateStatus,
    CurrentBridgeCandidateSource = c.CandidateSource,
    CurrentAcceptedAuthorDecisionId = ad.FacultyAuthorMatchDecisionId,
    CurrentAcceptedAuthorDecisionSource = ad.DecisionSource,
    i.ProposedCandidateAction,
    i.ProposedDecisionAction,
    i.PreviewStatus
FROM openalex.AcceptedIdentityBridgePreviewItem i
OUTER APPLY
(
    SELECT TOP (1)
        c2.FacultyAuthorMatchCandidateId,
        c2.CandidateStatus,
        c2.CandidateSource
    FROM openalex.FacultyAuthorMatchCandidate c2
    WHERE c2.FacultyId = i.FacultyId
      AND c2.AuthorOpenAlexId = i.CandidateAuthorOpenAlexId
      AND c2.CandidateSource = 'AcceptedIdentityDecision'
    ORDER BY c2.FacultyAuthorMatchCandidateId DESC
) c
OUTER APPLY
(
    SELECT TOP (1)
        d2.FacultyAuthorMatchDecisionId,
        d2.DecisionSource
    FROM openalex.FacultyAuthorMatchDecision d2
    WHERE d2.FacultyId = i.FacultyId
      AND d2.AuthorOpenAlexId = i.CandidateAuthorOpenAlexId
      AND d2.IsCurrent = 1
      AND d2.DecisionStatus = 'Accepted'
    ORDER BY d2.FacultyAuthorMatchDecisionId DESC
) ad
WHERE i.AcceptedIdentityBridgePreviewRunId = @PreviewRunId
ORDER BY i.CandidateDisplayName, i.CandidateAuthorOpenAlexId;

PRINT '7) Next-action counts';
SELECT
    ProposedNextAction,
    IdentityCount = COUNT(*)
FROM openalex.AcceptedIdentityBridgePreviewItem
WHERE AcceptedIdentityBridgePreviewRunId = @PreviewRunId
GROUP BY ProposedNextAction
ORDER BY ProposedNextAction;
