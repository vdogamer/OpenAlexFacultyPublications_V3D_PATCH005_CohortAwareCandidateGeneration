/*
V3E PATCH017B9 - Five-Faculty Accepted-Identity Bridge Execution Diagnostics
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestExecutionRunId BIGINT;

SELECT TOP (1)
    @LatestExecutionRunId = AcceptedIdentityCohortBridgeExecutionRunId
FROM openalex.AcceptedIdentityCohortBridgeExecutionRun
WHERE CohortName = @CohortName
ORDER BY AcceptedIdentityCohortBridgeExecutionRunId DESC;

IF @LatestExecutionRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B9 cohort bridge execution run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Latest PATCH017B9 execution run';
SELECT *
FROM openalex.AcceptedIdentityCohortBridgeExecutionRun
WHERE AcceptedIdentityCohortBridgeExecutionRunId = @LatestExecutionRunId;

PRINT '2) Accepted identity -> author bridge verification';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    d.CandidateAuthorOpenAlexId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.BestIdentityEvidenceScore,
    c.FacultyAuthorMatchCandidateId,
    c.CandidateSource,
    c.CandidateStatus,
    c.AuthorId,
    ad.FacultyAuthorMatchDecisionId,
    ad.DecisionStatus,
    ad.DecisionReason,
    ad.DecisionSource,
    ad.IsCurrent
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
INNER JOIN openalex.FacultyIdentityCandidateDecision d
    ON d.FacultyId = pc.FacultyId
   AND d.IsCurrent = 1
   AND d.DecisionStatus = 'AcceptedIdentity'
OUTER APPLY
(
    SELECT TOP (1)
        s2.CandidateDisplayName,
        s2.CandidateOrcid,
        s2.BestIdentityEvidenceScore
    FROM openalex.FacultyIdentityCandidateSummary s2
    WHERE s2.FacultyId = d.FacultyId
      AND s2.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
    ORDER BY s2.BestIdentityEvidenceScore DESC,
             s2.FacultyIdentityCandidateSummaryId DESC
) s
LEFT JOIN openalex.FacultyAuthorMatchCandidate c
    ON c.FacultyId = d.FacultyId
   AND c.AuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND c.CandidateSource = 'AcceptedIdentityDecision'
LEFT JOIN openalex.FacultyAuthorMatchDecision ad
    ON ad.FacultyId = d.FacultyId
   AND ad.AuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND ad.IsCurrent = 1
   AND ad.DecisionStatus = 'Accepted'
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY pc.PilotOrder, s.BestIdentityEvidenceScore DESC, d.CandidateAuthorOpenAlexId;

PRINT '3) Bridge counts by faculty';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    CurrentAcceptedIdentityCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyIdentityCandidateDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = 'AcceptedIdentity'
    ),
    BridgeCandidateCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchCandidate c
        WHERE c.FacultyId = f.FacultyId
          AND c.CandidateSource = 'AcceptedIdentityDecision'
    ),
    CurrentAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = 'Accepted'
    )
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY pc.PilotOrder;

PRINT '4) PATCH017B9 readiness summary';
DECLARE @RunStatus VARCHAR(30);
DECLARE @SelectedIdentityCount INT;
DECLARE @PublicationCandidateRowsBefore INT;
DECLARE @PublicationCandidateRowsAfter INT;
DECLARE @PublicationDecisionRowsBefore INT;
DECLARE @PublicationDecisionRowsAfter INT;

SELECT
    @RunStatus = RunStatus,
    @SelectedIdentityCount = SelectedAcceptedIdentityCount,
    @PublicationCandidateRowsBefore = PublicationCandidateRowsBefore,
    @PublicationCandidateRowsAfter = PublicationCandidateRowsAfter,
    @PublicationDecisionRowsBefore = PublicationDecisionRowsBefore,
    @PublicationDecisionRowsAfter = PublicationDecisionRowsAfter
FROM openalex.AcceptedIdentityCohortBridgeExecutionRun
WHERE AcceptedIdentityCohortBridgeExecutionRunId = @LatestExecutionRunId;

DECLARE @CurrentAcceptedIdentityCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityCandidateDecision d
    WHERE d.IsCurrent = 1
      AND d.DecisionStatus = 'AcceptedIdentity'
      AND EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.CohortName = @CohortName
            AND pc.IsEnabled = 1
            AND pc.FacultyId = d.FacultyId
      )
);

DECLARE @CurrentAcceptedAuthorDecisionCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyAuthorMatchDecision d
    WHERE d.IsCurrent = 1
      AND d.DecisionStatus = 'Accepted'
      AND EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.CohortName = @CohortName
            AND pc.IsEnabled = 1
            AND pc.FacultyId = d.FacultyId
      )
);

SELECT
    LatestExecutionRunId = @LatestExecutionRunId,
    RunStatus = @RunStatus,
    SelectedAcceptedIdentityCount = @SelectedIdentityCount,
    CurrentAcceptedIdentityCount = @CurrentAcceptedIdentityCount,
    CurrentAcceptedAuthorDecisionCount = @CurrentAcceptedAuthorDecisionCount,
    PublicationCandidateRowsBefore = @PublicationCandidateRowsBefore,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsBefore = @PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    SafetyStatus =
        CASE
            WHEN @PublicationCandidateRowsBefore = @PublicationCandidateRowsAfter
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'ControlledBridge_NoPublicationCandidateOrDecisionChange'
            ELSE 'InvestigatePublicationCountChange'
        END,
    NextAction =
        CASE
            WHEN @RunStatus = 'Completed'
             AND @SelectedIdentityCount = @CurrentAcceptedIdentityCount
             AND @CurrentAcceptedIdentityCount = @CurrentAcceptedAuthorDecisionCount
             AND @PublicationCandidateRowsBefore = @PublicationCandidateRowsAfter
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'ProceedToFiveFacultyPublicationCandidatePreview'
            WHEN @RunStatus <> 'Completed'
                THEN 'InspectPATCH017B9ExecutionFailure'
            ELSE 'ReviewBridgeCountMismatchBeforePublicationMatching'
        END;
