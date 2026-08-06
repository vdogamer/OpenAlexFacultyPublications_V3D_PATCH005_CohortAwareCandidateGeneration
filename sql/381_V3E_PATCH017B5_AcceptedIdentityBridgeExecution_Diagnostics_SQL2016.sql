/*
V3E PATCH017B5 - Controlled Accepted-Identity Bridge Execution Diagnostics
SQL Server 2016-safe and read-only.
*/

SET NOCOUNT ON;

DECLARE @ExecutionRunId BIGINT = NULL;

IF @ExecutionRunId IS NULL
BEGIN
    SELECT @ExecutionRunId = MAX(AcceptedIdentityBridgeExecutionRunId)
    FROM openalex.AcceptedIdentityBridgeExecutionRun;
END;

IF @ExecutionRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B5 execution run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Selected controlled bridge execution run';
SELECT
    r.AcceptedIdentityBridgeExecutionRunId,
    r.SourceAcceptedIdentityBridgePreviewRunId,
    r.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    r.MaxIdentities,
    r.ConfirmedBridge,
    r.IngestionRunId,
    r.SelectedIdentityCount,
    r.CandidateRowsInserted,
    r.CandidateRowsUpdated,
    r.DecisionRowsInserted,
    r.DecisionsAlreadyAccepted,
    r.MissingLocalAuthorCount,
    r.AuthorCandidateRowsBefore,
    r.AuthorCandidateRowsAfter,
    r.AuthorDecisionRowsBefore,
    r.AuthorDecisionRowsAfter,
    r.PublicationCandidateRowsBefore,
    r.PublicationCandidateRowsAfter,
    r.PublicationDecisionRowsBefore,
    r.PublicationDecisionRowsAfter,
    r.RunStatus,
    r.StartedBy,
    r.ApplicationVersion,
    r.SummaryText,
    r.ErrorText,
    r.StartedAtUtc,
    r.CompletedAtUtc
FROM openalex.AcceptedIdentityBridgeExecutionRun r
LEFT JOIN openalex.Faculty f ON f.FacultyId = r.FacultyId
WHERE r.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId;

PRINT '2) Execution action counts';
SELECT
    i.CandidateExecutionAction,
    i.DecisionExecutionAction,
    i.ExecutionStatus,
    ExecutionItemCount = COUNT(*)
FROM openalex.AcceptedIdentityBridgeExecutionItem i
WHERE i.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId
GROUP BY
    i.CandidateExecutionAction,
    i.DecisionExecutionAction,
    i.ExecutionStatus
ORDER BY
    i.CandidateExecutionAction,
    i.DecisionExecutionAction;

PRINT '3) Detailed controlled bridge items';
SELECT
    i.AcceptedIdentityBridgeExecutionItemId,
    i.SourceAcceptedIdentityBridgePreviewItemId,
    i.FacultyIdentityCandidateDecisionId,
    i.FacultyIdentityCandidateSummaryId,
    i.FacultyId,
    i.FacultyDisplayName,
    i.CandidateAuthorOpenAlexId,
    i.CandidateAuthorOpenAlexShortId,
    i.CandidateDisplayName,
    i.CandidateOrcid,
    i.LocalAuthorId,
    i.BridgeCandidateIdBefore,
    i.BridgeCandidateIdAfter,
    i.AcceptedAuthorDecisionIdBefore,
    i.AcceptedAuthorDecisionIdAfter,
    i.CandidateExecutionAction,
    i.DecisionExecutionAction,
    i.ExecutionStatus,
    i.WarningText,
    i.CreatedAtUtc
FROM openalex.AcceptedIdentityBridgeExecutionItem i
WHERE i.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId
ORDER BY i.AcceptedIdentityBridgeExecutionItemId;

PRINT '4) Current faculty-author candidate and accepted-decision verification';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    i.CandidateAuthorOpenAlexId,
    i.CandidateDisplayName,
    CandidateIdExpected = i.BridgeCandidateIdAfter,
    CurrentCandidateId = c.FacultyAuthorMatchCandidateId,
    c.CandidateSource,
    c.CandidateStatus,
    c.MatchScore,
    c.AuthorId,
    AcceptedDecisionIdExpected = i.AcceptedAuthorDecisionIdAfter,
    CurrentAcceptedDecisionId = d.FacultyAuthorMatchDecisionId,
    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.IsCurrent,
    d.ReviewedBy,
    VerificationStatus = CASE
        WHEN c.FacultyAuthorMatchCandidateId = i.BridgeCandidateIdAfter
         AND c.CandidateSource = 'AcceptedIdentityDecision'
         AND c.CandidateStatus = 'AutoAcceptedCandidate'
         AND d.FacultyAuthorMatchDecisionId = i.AcceptedAuthorDecisionIdAfter
         AND d.DecisionStatus = 'Accepted'
         AND d.IsCurrent = 1
        THEN 'BridgeVerified'
        ELSE 'BridgeVerificationMismatch'
    END
FROM openalex.AcceptedIdentityBridgeExecutionItem i
LEFT JOIN openalex.FacultyAuthorMatchCandidate c
    ON c.FacultyAuthorMatchCandidateId = i.BridgeCandidateIdAfter
LEFT JOIN openalex.FacultyAuthorMatchDecision d
    ON d.FacultyAuthorMatchDecisionId = i.AcceptedAuthorDecisionIdAfter
WHERE i.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId
ORDER BY i.AcceptedIdentityBridgeExecutionItemId;

PRINT '5) Candidate, author-decision, and publication safety verification';
SELECT
    r.AuthorCandidateRowsBefore,
    r.AuthorCandidateRowsAfter,
    ExpectedAuthorCandidateRowsAfter = r.AuthorCandidateRowsBefore + r.CandidateRowsInserted,
    AuthorCandidateCountMatches = CASE
        WHEN r.AuthorCandidateRowsAfter = r.AuthorCandidateRowsBefore + r.CandidateRowsInserted THEN 1 ELSE 0 END,
    r.AuthorDecisionRowsBefore,
    r.AuthorDecisionRowsAfter,
    ExpectedAuthorDecisionRowsAfter = r.AuthorDecisionRowsBefore + r.DecisionRowsInserted,
    AuthorDecisionCountMatches = CASE
        WHEN r.AuthorDecisionRowsAfter = r.AuthorDecisionRowsBefore + r.DecisionRowsInserted THEN 1 ELSE 0 END,
    r.PublicationCandidateRowsBefore,
    r.PublicationCandidateRowsAfter,
    PublicationCandidateRowsUnchanged = CASE
        WHEN r.PublicationCandidateRowsBefore = r.PublicationCandidateRowsAfter THEN 1 ELSE 0 END,
    r.PublicationDecisionRowsBefore,
    r.PublicationDecisionRowsAfter,
    PublicationDecisionRowsUnchanged = CASE
        WHEN r.PublicationDecisionRowsBefore = r.PublicationDecisionRowsAfter THEN 1 ELSE 0 END,
    SafetyStatus = CASE
        WHEN r.RunStatus <> 'Completed' THEN 'NotEvaluated_RunIncomplete'
        WHEN r.AuthorCandidateRowsAfter <> r.AuthorCandidateRowsBefore + r.CandidateRowsInserted
          OR r.AuthorDecisionRowsAfter <> r.AuthorDecisionRowsBefore + r.DecisionRowsInserted
            THEN 'UnexpectedAuthorBridgeCountChange'
        WHEN r.PublicationCandidateRowsBefore <> r.PublicationCandidateRowsAfter
          OR r.PublicationDecisionRowsBefore <> r.PublicationDecisionRowsAfter
            THEN 'UnexpectedPublicationCountChange'
        ELSE 'ControlledBridge_NoPublicationCandidateOrDecisionChange'
    END
FROM openalex.AcceptedIdentityBridgeExecutionRun r
WHERE r.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId;

PRINT '6) Accepted identity to accepted author-decision coverage';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    i.CandidateAuthorOpenAlexId,
    i.CandidateDisplayName,
    IdentityDecisionStatus = idd.DecisionStatus,
    IdentityDecisionIsCurrent = idd.IsCurrent,
    AuthorDecisionStatus = ad.DecisionStatus,
    AuthorDecisionIsCurrent = ad.IsCurrent,
    AuthorDecisionSource = ad.DecisionSource,
    CoverageStatus = CASE
        WHEN idd.DecisionStatus = 'AcceptedIdentity'
         AND idd.IsCurrent = 1
         AND ad.DecisionStatus = 'Accepted'
         AND ad.IsCurrent = 1
            THEN 'AcceptedIdentitySuccessfullyBridged'
        ELSE 'AcceptedIdentityBridgeGap'
    END
FROM openalex.AcceptedIdentityBridgeExecutionItem i
LEFT JOIN openalex.FacultyIdentityCandidateDecision idd
    ON idd.FacultyIdentityCandidateDecisionId = i.FacultyIdentityCandidateDecisionId
LEFT JOIN openalex.FacultyAuthorMatchDecision ad
    ON ad.FacultyAuthorMatchDecisionId = i.AcceptedAuthorDecisionIdAfter
WHERE i.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId
ORDER BY i.AcceptedIdentityBridgeExecutionItemId;

PRINT '7) Next action';
SELECT
    r.AcceptedIdentityBridgeExecutionRunId,
    r.RunStatus,
    ReadyAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.AcceptedIdentityBridgeExecutionItem i
        JOIN openalex.FacultyAuthorMatchDecision d
            ON d.FacultyAuthorMatchDecisionId = i.AcceptedAuthorDecisionIdAfter
        WHERE i.AcceptedIdentityBridgeExecutionRunId = r.AcceptedIdentityBridgeExecutionRunId
          AND d.DecisionStatus = 'Accepted'
          AND d.IsCurrent = 1
    ),
    NextAction = CASE
        WHEN r.RunStatus <> 'Completed' THEN 'ResolvePATCH017B5ExecutionFailure'
        WHEN r.PublicationCandidateRowsBefore <> r.PublicationCandidateRowsAfter
          OR r.PublicationDecisionRowsBefore <> r.PublicationDecisionRowsAfter
            THEN 'InvestigateUnexpectedPublicationCountChange'
        ELSE 'RerunPATCH017B2FacultyPublicationCandidatePreview'
    END
FROM openalex.AcceptedIdentityBridgeExecutionRun r
WHERE r.AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId;
