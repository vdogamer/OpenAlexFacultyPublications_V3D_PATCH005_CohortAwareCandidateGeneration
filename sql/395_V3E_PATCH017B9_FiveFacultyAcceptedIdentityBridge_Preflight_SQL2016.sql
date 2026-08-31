/*
V3E PATCH017B9 - Five-Faculty Accepted-Identity Bridge Preflight
SQL Server 2016 compatible.

Purpose
-------
Read-only cohort-scale preview of current AcceptedIdentity decisions before they
are bridged into openalex.FacultyAuthorMatchCandidate / FacultyAuthorMatchDecision.

This script does NOT insert/update/delete matching data.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateDecision does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateSummary does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL
BEGIN
    RAISERROR('Author-match candidate/decision tables do not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('tempdb..#AcceptedIdentityBridgePreview') IS NOT NULL
    DROP TABLE #AcceptedIdentityBridgePreview;

;WITH CohortFaculty AS
(
    SELECT
        pc.PilotOrder,
        pc.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        f.Orcid AS FacultyOrcid,
        f.ClaimedOpenAlexAuthorId
    FROM openalex.PilotFacultyCohort pc
    INNER JOIN openalex.Faculty f
        ON f.FacultyId = pc.FacultyId
    WHERE pc.CohortName = @CohortName
      AND pc.IsEnabled = 1
),
AcceptedIdentity AS
(
    SELECT
        cf.PilotOrder,
        cf.FacultyId,
        cf.FacultyDisplayName,
        d.FacultyIdentityCandidateDecisionId,
        d.CandidateAuthorOpenAlexId,
        d.CandidateAuthorOpenAlexShortId
    FROM CohortFaculty cf
    INNER JOIN openalex.FacultyIdentityCandidateDecision d
        ON d.FacultyId = cf.FacultyId
       AND d.IsCurrent = 1
       AND d.DecisionStatus = 'AcceptedIdentity'
)
SELECT
    ai.PilotOrder,
    ai.FacultyId,
    ai.FacultyDisplayName,
    ai.FacultyIdentityCandidateDecisionId,
    ai.CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId =
        COALESCE(s.CandidateAuthorOpenAlexShortId, ai.CandidateAuthorOpenAlexShortId),
    CandidateDisplayName = s.CandidateDisplayName,
    CandidateOrcid = s.CandidateOrcid,
    BestIdentityEvidenceScore = s.BestIdentityEvidenceScore,
    FacultyIdentityCandidateSummaryId = s.FacultyIdentityCandidateSummaryId,
    LocalAuthorId = a.AuthorId,
    ExistingBridgeCandidateId = c.FacultyAuthorMatchCandidateId,
    ExistingBridgeCandidateStatus = c.CandidateStatus,
    ExistingAcceptedAuthorDecisionId = ad.FacultyAuthorMatchDecisionId,
    ProposedCandidateAction =
        CASE
            WHEN c.FacultyAuthorMatchCandidateId IS NULL THEN 'InsertAcceptedIdentityCandidate'
            ELSE 'UpdateAcceptedIdentityCandidate'
        END,
    ProposedDecisionAction =
        CASE
            WHEN ad.FacultyAuthorMatchDecisionId IS NULL THEN 'InsertAcceptedAuthorDecision'
            ELSE 'SkipAlreadyAcceptedDecision'
        END,
    PreviewStatus =
        CASE
            WHEN s.FacultyIdentityCandidateSummaryId IS NULL THEN 'BlockedMissingIdentitySummary'
            WHEN a.AuthorId IS NULL THEN 'ReadyToBridgeWithMissingLocalAuthorWarning'
            WHEN c.FacultyAuthorMatchCandidateId IS NOT NULL
             AND ad.FacultyAuthorMatchDecisionId IS NOT NULL THEN 'AlreadyBridged'
            WHEN c.FacultyAuthorMatchCandidateId IS NOT NULL THEN 'ReadyToRefreshCandidateAndCreateDecision'
            WHEN ad.FacultyAuthorMatchDecisionId IS NOT NULL THEN 'ReadyToCreateBridgeCandidateOnly'
            ELSE 'ReadyToCreateCandidateAndDecision'
        END
INTO #AcceptedIdentityBridgePreview
FROM AcceptedIdentity ai
OUTER APPLY
(
    SELECT TOP (1)
        s2.FacultyIdentityCandidateSummaryId,
        s2.CandidateAuthorOpenAlexShortId,
        s2.CandidateDisplayName,
        s2.CandidateOrcid,
        s2.BestIdentityEvidenceScore
    FROM openalex.FacultyIdentityCandidateSummary s2
    WHERE s2.FacultyId = ai.FacultyId
      AND s2.CandidateAuthorOpenAlexId = ai.CandidateAuthorOpenAlexId
    ORDER BY
        s2.BestIdentityEvidenceScore DESC,
        s2.FacultyIdentityCandidateSummaryId DESC
) s
OUTER APPLY
(
    SELECT TOP (1)
        a2.AuthorId
    FROM openalex.Author a2
    WHERE
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.OpenAlexId)),
            'https://openalex.org/', ''), 'http://openalex.org/', '')) =
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ai.CandidateAuthorOpenAlexId)),
            'https://openalex.org/', ''), 'http://openalex.org/', ''))
        OR
        (
            s.CandidateAuthorOpenAlexShortId IS NOT NULL
            AND a2.OpenAlexShortId = s.CandidateAuthorOpenAlexShortId
        )
        OR
        (
            s.CandidateOrcid IS NOT NULL
            AND a2.Orcid IS NOT NULL
            AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.Orcid)),
                'https://orcid.org/', ''), 'http://orcid.org/', '')) =
                UPPER(REPLACE(REPLACE(LTRIM(RTRIM(s.CandidateOrcid)),
                'https://orcid.org/', ''), 'http://orcid.org/', ''))
        )
    ORDER BY a2.AuthorId
) a
OUTER APPLY
(
    SELECT TOP (1)
        c2.FacultyAuthorMatchCandidateId,
        c2.CandidateStatus
    FROM openalex.FacultyAuthorMatchCandidate c2
    WHERE c2.FacultyId = ai.FacultyId
      AND c2.AuthorOpenAlexId = ai.CandidateAuthorOpenAlexId
      AND c2.CandidateSource = 'AcceptedIdentityDecision'
    ORDER BY c2.FacultyAuthorMatchCandidateId DESC
) c
OUTER APPLY
(
    SELECT TOP (1)
        d2.FacultyAuthorMatchDecisionId
    FROM openalex.FacultyAuthorMatchDecision d2
    WHERE d2.FacultyId = ai.FacultyId
      AND d2.AuthorOpenAlexId = ai.CandidateAuthorOpenAlexId
      AND d2.IsCurrent = 1
      AND d2.DecisionStatus = 'Accepted'
    ORDER BY d2.FacultyAuthorMatchDecisionId DESC
) ad;

PRINT '1) Cohort identity-review completion / accepted-identity counts';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    ActionableSummaryCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyIdentityCandidateSummary s
        WHERE s.FacultyId = f.FacultyId
          AND s.IdentityReviewTriageStatus IN
              ('LikelyIdentity', 'PossibleSplitProfile', 'NeedsManualReview')
    ),
    CurrentAcceptedIdentityCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyIdentityCandidateDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = 'AcceptedIdentity'
    ),
    CurrentOtherIdentityDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyIdentityCandidateDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus <> 'AcceptedIdentity'
    )
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY pc.PilotOrder;

PRINT '2) Accepted identities and proposed bridge actions';
SELECT *
FROM #AcceptedIdentityBridgePreview
ORDER BY PilotOrder, BestIdentityEvidenceScore DESC, CandidateAuthorOpenAlexId;

PRINT '3) Bridge action counts by faculty';
SELECT
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    AcceptedIdentityCount = COUNT(*),
    CandidateInsertCount = SUM(CASE WHEN ExistingBridgeCandidateId IS NULL THEN 1 ELSE 0 END),
    CandidateUpdateCount = SUM(CASE WHEN ExistingBridgeCandidateId IS NOT NULL THEN 1 ELSE 0 END),
    DecisionInsertCount = SUM(CASE WHEN ExistingAcceptedAuthorDecisionId IS NULL THEN 1 ELSE 0 END),
    AlreadyAcceptedDecisionCount = SUM(CASE WHEN ExistingAcceptedAuthorDecisionId IS NOT NULL THEN 1 ELSE 0 END),
    MissingIdentitySummaryCount = SUM(CASE WHEN FacultyIdentityCandidateSummaryId IS NULL THEN 1 ELSE 0 END),
    MissingLocalAuthorCount = SUM(CASE WHEN LocalAuthorId IS NULL THEN 1 ELSE 0 END)
FROM #AcceptedIdentityBridgePreview
GROUP BY PilotOrder, FacultyId, FacultyDisplayName
ORDER BY PilotOrder;

PRINT '4) Safety baseline before bridge execution';
SELECT
    AuthorCandidateRows =
        (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchCandidate),
    AuthorDecisionRows =
        (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchDecision),
    PublicationCandidateRows =
        CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
             THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END,
    PublicationDecisionRows =
        CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
             THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END,
    SafetyStatus = 'PreviewOnly_NoMatchingDataChanged';

PRINT '5) PATCH017B9 readiness summary';
DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithAcceptedIdentity INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM #AcceptedIdentityBridgePreview
);

DECLARE @AcceptedIdentityCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
);

DECLARE @BlockedCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
    WHERE PreviewStatus = 'BlockedMissingIdentitySummary'
);

DECLARE @CandidateInsertCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
    WHERE ExistingBridgeCandidateId IS NULL
      AND FacultyIdentityCandidateSummaryId IS NOT NULL
);

DECLARE @CandidateUpdateCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
    WHERE ExistingBridgeCandidateId IS NOT NULL
      AND FacultyIdentityCandidateSummaryId IS NOT NULL
);

DECLARE @DecisionInsertCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
    WHERE ExistingAcceptedAuthorDecisionId IS NULL
      AND FacultyIdentityCandidateSummaryId IS NOT NULL
);

DECLARE @AlreadyAcceptedCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
    WHERE ExistingAcceptedAuthorDecisionId IS NOT NULL
);

DECLARE @MissingLocalAuthorCount INT =
(
    SELECT COUNT(*)
    FROM #AcceptedIdentityBridgePreview
    WHERE LocalAuthorId IS NULL
);

SELECT
    CohortName = @CohortName,
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithAcceptedIdentity = @FacultyWithAcceptedIdentity,
    FacultyWithZeroAcceptedIdentity = @EnabledFacultyCount - @FacultyWithAcceptedIdentity,
    AcceptedIdentityCount = @AcceptedIdentityCount,
    CandidateInsertCount = @CandidateInsertCount,
    CandidateUpdateCount = @CandidateUpdateCount,
    DecisionInsertCount = @DecisionInsertCount,
    AlreadyAcceptedDecisionCount = @AlreadyAcceptedCount,
    MissingIdentitySummaryCount = @BlockedCount,
    MissingLocalAuthorCount = @MissingLocalAuthorCount,
    SafetyStatus = 'PreviewOnly_NoMatchingDataChanged',
    NextAction =
        CASE
            WHEN @AcceptedIdentityCount = 0
                THEN 'CompleteIdentityReviewBeforeBridge'
            WHEN @BlockedCount > 0
                THEN 'RepairMissingIdentitySummariesBeforeBridge'
            WHEN @FacultyWithAcceptedIdentity < @EnabledFacultyCount
                THEN 'ReviewFacultyWithZeroAcceptedIdentityBeforeBridge'
            ELSE 'ProceedToControlledFiveFacultyAcceptedIdentityBridge'
        END;

DROP TABLE #AcceptedIdentityBridgePreview;
