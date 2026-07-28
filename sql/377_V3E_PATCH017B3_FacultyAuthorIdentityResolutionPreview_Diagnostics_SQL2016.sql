/*
V3E PATCH017B3 - Faculty-Author Identity Resolution Preview Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

DECLARE @PreviewRunId BIGINT = NULL;

IF OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewRun', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewCandidate', N'U') IS NULL
BEGIN
    SELECT 'PATCH017B3 preview tables are missing. Run sql/376_V3E_PATCH017B3_FacultyAuthorIdentityResolutionPreview_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT @PreviewRunId = MAX(FacultyAuthorIdentityResolutionPreviewRunId)
    FROM openalex.FacultyAuthorIdentityResolutionPreviewRun;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT 'No PATCH017B3 preview runs were found.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Selected PATCH017B3 identity resolution preview run';
SELECT
    FacultyAuthorIdentityResolutionPreviewRunId,
    SourceWorkBackfillRunId,
    FacultyId,
    MaxWorks,
    SelectedWorkCount,
    EvaluatedCandidateCount,
    StrongCandidateCount,
    ExactOrcidCandidateCount,
    AmbiguousCandidateCount,
    IdentityCandidateRowsBefore,
    IdentityCandidateRowsAfter,
    IdentityDecisionRowsBefore,
    IdentityDecisionRowsAfter,
    AuthorDecisionRowsBefore,
    AuthorDecisionRowsAfter,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    SummaryText,
    ErrorText,
    StartedAtUtc,
    CompletedAtUtc
FROM openalex.FacultyAuthorIdentityResolutionPreviewRun
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId;

PRINT '2) Preview status counts';
SELECT
    PreviewStatus,
    ProposedNextAction,
    PreviewCandidateCount = COUNT(*)
FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
GROUP BY PreviewStatus, ProposedNextAction
ORDER BY MIN(CandidateRank), PreviewStatus;

PRINT '3) Ranked identity candidates';
SELECT
    CandidateRank,
    FacultyId,
    FacultyDisplayName,
    FacultyOrcid,
    AcademicOnlineName,
    AcademicOnlineEmail,
    AcademicOnlineOrcidUrl,
    CandidateAuthorOpenAlexId,
    CandidateAuthorDisplayName,
    CandidateAuthorOrcid,
    ExactFacultyOrcidMatch,
    ExactAcademicOnlineOrcidMatch,
    ExactClaimedOpenAlexIdMatch,
    NameEvidenceScore,
    WorkAppearanceCount,
    SourceWorkCount,
    AppearsOnAllSelectedWorks,
    CorrespondingAuthorWorkCount,
    AuthorshipInstitutionCount,
    UvaAffiliationWorkCount,
    ProposedIdentityScore,
    PreviewStatus,
    ProposedNextAction,
    EvidenceSummary
FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
ORDER BY CandidateRank;

PRINT '4) ORCID and claimed OpenAlex ID evidence';
SELECT
    CandidateRank,
    FacultyDisplayName,
    FacultyOrcid,
    AcademicOnlineOrcidUrl,
    FacultyClaimedOpenAlexAuthorId,
    CandidateAuthorDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOrcid,
    ExactFacultyOrcidMatch,
    ExactAcademicOnlineOrcidMatch,
    ExactClaimedOpenAlexIdMatch,
    ProposedIdentityScore,
    PreviewStatus
FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
ORDER BY CandidateRank;

PRINT '5) Cross-work authorship coverage for ranked candidates';
;WITH SelectedRun AS
(
    SELECT SourceWorkBackfillRunId, FacultyId
    FROM openalex.FacultyAuthorIdentityResolutionPreviewRun
    WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
),
SelectedWorks AS
(
    SELECT DISTINCT
        bi.WorkIdAfter AS WorkId,
        bi.NormalizedOpenAlexId
    FROM openalex.AcademicOnlineWorkBackfillItem bi
    JOIN SelectedRun sr
        ON sr.SourceWorkBackfillRunId = bi.AcademicOnlineWorkBackfillRunId
       AND sr.FacultyId = bi.FacultyId
    WHERE bi.WorkIdAfter IS NOT NULL
      AND bi.ExecutionStatus IN ('Inserted', 'ConcurrentExistingWorkUpdated', 'SkippedAlreadyExists')
)
SELECT
    c.CandidateRank,
    c.CandidateAuthorDisplayName,
    c.CandidateAuthorOpenAlexId,
    w.WorkId,
    w.OpenAlexId AS WorkOpenAlexId,
    COALESCE(w.Title, w.DisplayName) AS WorkTitle,
    wa.WorkAuthorshipId,
    AcceptedCandidateAppearsOnWork = CASE WHEN wa.WorkAuthorshipId IS NULL THEN 0 ELSE 1 END,
    wa.OrdinalPosition,
    wa.IsCorresponding,
    wa.AuthorOrcid,
    UvaAffiliationCount =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
          AND
          (
              wai.InstitutionDisplayName LIKE '%University of Virginia%'
              OR wai.InstitutionOpenAlexId IN
              (
                  'https://openalex.org/I4210107925',
                  'https://openalex.org/I4210119250',
                  'https://openalex.org/I4210134211'
              )
          )
    )
FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate c
CROSS JOIN SelectedWorks sw
JOIN openalex.Work w ON w.WorkId = sw.WorkId
LEFT JOIN openalex.WorkAuthorship wa
    ON wa.WorkId = w.WorkId
   AND wa.AuthorOpenAlexId = c.CandidateAuthorOpenAlexId
WHERE c.FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
ORDER BY c.CandidateRank, w.PublicationYear DESC, w.WorkId;

PRINT '6) Existing identity-review and author-decision state';
SELECT
    CandidateRank,
    CandidateAuthorDisplayName,
    CandidateAuthorOpenAlexId,
    ExistingIdentitySummaryId,
    ExistingIdentityTriageStatus,
    ExistingIdentityEvidenceScore,
    ExistingIdentityCandidateDiscoveryCount,
    CurrentIdentityDecisionStatus,
    CurrentAcceptedAuthorDecisionCount,
    PreviewStatus,
    ProposedNextAction
FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
ORDER BY CandidateRank;

PRINT '7) Preview-only safety verification';
SELECT
    IdentityCandidateRowsBefore,
    IdentityCandidateRowsAfter,
    IdentityCandidateRowsUnchanged = CASE WHEN IdentityCandidateRowsBefore = IdentityCandidateRowsAfter THEN 1 ELSE 0 END,
    IdentityDecisionRowsBefore,
    IdentityDecisionRowsAfter,
    IdentityDecisionRowsUnchanged = CASE WHEN IdentityDecisionRowsBefore = IdentityDecisionRowsAfter THEN 1 ELSE 0 END,
    AuthorDecisionRowsBefore,
    AuthorDecisionRowsAfter,
    AuthorDecisionRowsUnchanged = CASE WHEN AuthorDecisionRowsBefore = AuthorDecisionRowsAfter THEN 1 ELSE 0 END,
    SafetyStatus = CASE
        WHEN RunStatus <> 'Completed' THEN 'NotEvaluated_RunIncomplete'
        WHEN IdentityCandidateRowsBefore = IdentityCandidateRowsAfter
         AND IdentityDecisionRowsBefore = IdentityDecisionRowsAfter
         AND AuthorDecisionRowsBefore = AuthorDecisionRowsAfter
            THEN 'PreviewOnly_NoIdentityOrAuthorDecisionChange'
        ELSE 'UnexpectedIdentityOrAuthorDecisionCountChange'
    END
FROM openalex.FacultyAuthorIdentityResolutionPreviewRun
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId;

PRINT '8) Recommended next action';
SELECT TOP (10)
    CandidateRank,
    CandidateAuthorDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOrcid,
    ProposedIdentityScore,
    PreviewStatus,
    ProposedNextAction,
    NextActionExplanation = CASE ProposedNextAction
        WHEN 'IdentityAlreadyAccepted_RerunPATCH017B2' THEN 'The identity is already accepted. Rerun PATCH017B2 to preview publication-candidate generation.'
        WHEN 'ReviewExistingIdentityCandidate' THEN 'Open the existing Identity Review record and accept or reject it using the normal human-review workflow.'
        WHEN 'RunIdentityCandidatePipelineForFaculty' THEN 'Run the existing identity candidate discovery, evidence scoring, and summary pipeline for this faculty before human review.'
        WHEN 'ManualIdentityInvestigation' THEN 'More than one strong author candidate exists. Resolve the ambiguity manually before accepting an identity.'
        ELSE 'Collect more identity evidence before accepting an OpenAlex author identity.'
    END
FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId
ORDER BY CandidateRank;
