/*
V3E PATCH016A - Academic Online Comparison Diagnostics Fix
SQL Server 2016-safe.

Purpose:
  Improve PATCH016 diagnostics by:
    1. Restricting comparison to faculty included in the selected Academic Online run.
    2. Comparing Academic Online publications to local openalex.Work rows, not only FacultyPublicationCandidate rows.
    3. Separating local work existence from local faculty/publication candidate existence.
    4. Showing whether our app accepted the same publication for the same faculty.
    5. Providing per-faculty summary counts.

Usage:
  1. Run sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql
  2. Run the AcademicOnlineComparison worker job
  3. Run this diagnostic script
*/

SET NOCOUNT ON;

DECLARE @RunId BIGINT = NULL;
DECLARE @FacultyId BIGINT = NULL;

IF OBJECT_ID(N'openalex.AcademicOnlineComparisonRun', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlineComparisonRun. Run sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlineScholarSnapshot. Run sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlinePublicationSnapshot. Run sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.Work', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.Work. Run OpenAlex Work ingestion first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationCandidate. Run publication candidate generation first.' AS DiagnosticMessage;
    RETURN;
END;

IF @RunId IS NULL
BEGIN
    SELECT @RunId = MAX(AcademicOnlineComparisonRunId)
    FROM openalex.AcademicOnlineComparisonRun;
END;

IF @RunId IS NULL
BEGIN
    SELECT 'No Academic Online comparison runs found yet.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID('tempdb..#RunFaculty') IS NOT NULL DROP TABLE #RunFaculty;
IF OBJECT_ID('tempdb..#AcademicOnlineComparison') IS NOT NULL DROP TABLE #AcademicOnlineComparison;
IF OBJECT_ID('tempdb..#LocalOpenAlexOnly') IS NOT NULL DROP TABLE #LocalOpenAlexOnly;

CREATE TABLE #RunFaculty
(
    FacultyId BIGINT NOT NULL PRIMARY KEY,
    FacultyDisplayName NVARCHAR(512) NOT NULL,
    DepartmentName NVARCHAR(512) NULL,
    AcademicOnlineAuthorId NVARCHAR(128) NULL,
    AcademicOnlineName NVARCHAR(512) NULL,
    AcademicOnlineEmail NVARCHAR(512) NULL,
    PublicationCount INT NULL,
    HasVerifiedPublications BIT NULL
);

INSERT INTO #RunFaculty
(
    FacultyId,
    FacultyDisplayName,
    DepartmentName,
    AcademicOnlineAuthorId,
    AcademicOnlineName,
    AcademicOnlineEmail,
    PublicationCount,
    HasVerifiedPublications
)
SELECT
    s.FacultyId,
    MAX(s.FacultyDisplayName),
    MAX(s.DepartmentName),
    MAX(s.AcademicOnlineAuthorId),
    MAX(s.AcademicOnlineName),
    MAX(s.AcademicOnlineEmail),
    MAX(s.PublicationCount),
    MAX(CONVERT(INT, s.HasVerifiedPublications))
FROM openalex.AcademicOnlineScholarSnapshot s
WHERE s.AcademicOnlineComparisonRunId = @RunId
  AND (@FacultyId IS NULL OR s.FacultyId = @FacultyId)
GROUP BY s.FacultyId;

PRINT '1) Selected Academic Online comparison run';
SELECT
    AcademicOnlineComparisonRunId,
    RunMode,
    PilotCohortName,
    MaxFacultyToProcess,
    StartedBy,
    ApplicationVersion,
    RunStatus,
    SummaryText,
    ErrorText,
    StartedAtUtc,
    CompletedAtUtc
FROM openalex.AcademicOnlineComparisonRun
WHERE AcademicOnlineComparisonRunId = @RunId;

PRINT '2) Faculty included in this Academic Online run';
SELECT
    rf.FacultyId,
    rf.FacultyDisplayName,
    rf.DepartmentName,
    rf.AcademicOnlineAuthorId,
    rf.AcademicOnlineName,
    rf.AcademicOnlineEmail,
    rf.PublicationCount,
    HasVerifiedPublications = CONVERT(BIT, rf.HasVerifiedPublications)
FROM #RunFaculty rf
ORDER BY rf.FacultyDisplayName;

CREATE TABLE #AcademicOnlineComparison
(
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NULL,
    AcademicOnlineAuthorId NVARCHAR(128) NULL,
    IsVerifiedQuery BIT NOT NULL,
    AcademicOnlinePublicationId NVARCHAR(128) NULL,
    AcademicOnlineTitle NVARCHAR(MAX) NULL,
    AcademicOnlineYear INT NULL,
    AcademicOnlineDoi NVARCHAR(512) NULL,
    AcademicOnlineOpenAlexId NVARCHAR(128) NULL,
    AcademicOnlineVerified BIT NULL,
    LocalWorkExists BIT NOT NULL,
    LocalWorkId BIGINT NULL,
    LocalWorkOpenAlexId NVARCHAR(128) NULL,
    LocalWorkTitle NVARCHAR(MAX) NULL,
    LocalCandidateExists BIT NOT NULL,
    LocalCandidateId BIGINT NULL,
    LocalCandidateStatus NVARCHAR(100) NULL,
    LocalMatchScore DECIMAL(18,4) NULL,
    LocalAcceptedDecisionExists BIT NOT NULL,
    LocalDecisionStatus NVARCHAR(100) NULL,
    MatchMethod VARCHAR(50) NOT NULL,
    ComparisonStatus VARCHAR(100) NOT NULL,
    ComparisonReason NVARCHAR(1000) NOT NULL
);

;WITH AcademicOnlinePublications AS
(
    SELECT
        p.AcademicOnlinePublicationSnapshotId,
        p.FacultyId,
        rf.FacultyDisplayName,
        p.AcademicOnlineAuthorId,
        p.IsVerifiedQuery,
        p.AcademicOnlinePublicationId,
        p.Title,
        p.PublicationYear,
        p.Doi,
        p.OpenAlexId,
        p.Verified,
        NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedAcademicTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''))
    FROM openalex.AcademicOnlinePublicationSnapshot p
    JOIN #RunFaculty rf
        ON p.FacultyId = rf.FacultyId
    WHERE p.AcademicOnlineComparisonRunId = @RunId
      AND (@FacultyId IS NULL OR p.FacultyId = @FacultyId)
)
INSERT INTO #AcademicOnlineComparison
(
    FacultyId,
    FacultyDisplayName,
    AcademicOnlineAuthorId,
    IsVerifiedQuery,
    AcademicOnlinePublicationId,
    AcademicOnlineTitle,
    AcademicOnlineYear,
    AcademicOnlineDoi,
    AcademicOnlineOpenAlexId,
    AcademicOnlineVerified,
    LocalWorkExists,
    LocalWorkId,
    LocalWorkOpenAlexId,
    LocalWorkTitle,
    LocalCandidateExists,
    LocalCandidateId,
    LocalCandidateStatus,
    LocalMatchScore,
    LocalAcceptedDecisionExists,
    LocalDecisionStatus,
    MatchMethod,
    ComparisonStatus,
    ComparisonReason
)
SELECT
    a.FacultyId,
    a.FacultyDisplayName,
    a.AcademicOnlineAuthorId,
    a.IsVerifiedQuery,
    a.AcademicOnlinePublicationId,
    a.Title,
    a.PublicationYear,
    a.Doi,
    a.OpenAlexId,
    a.Verified,
    LocalWorkExists = CASE WHEN lw.WorkId IS NULL THEN 0 ELSE 1 END,
    lw.WorkId,
    lw.OpenAlexId,
    lw.Title,
    LocalCandidateExists = CASE WHEN lc.FacultyPublicationCandidateId IS NULL THEN 0 ELSE 1 END,
    lc.FacultyPublicationCandidateId,
    lc.CandidateStatus,
    lc.MatchScore,
    LocalAcceptedDecisionExists = CASE WHEN lad.FacultyPublicationDecisionId IS NULL THEN 0 ELSE 1 END,
    lad.DecisionStatus,
    MatchMethod = CASE
        WHEN lw.MatchMethod IS NOT NULL THEN lw.MatchMethod
        WHEN lc.MatchMethod IS NOT NULL THEN lc.MatchMethod
        ELSE 'NoLocalMatch'
    END,
    ComparisonStatus = CASE
        WHEN lad.FacultyPublicationDecisionId IS NOT NULL THEN 'AcademicOnlineAcceptedByUs'
        WHEN lc.FacultyPublicationCandidateId IS NOT NULL THEN 'AcademicOnlineCandidateExists_NotAccepted'
        WHEN lw.WorkId IS NOT NULL THEN 'AcademicOnlineWorkExists_NoCandidate'
        ELSE 'AcademicOnlineOnly_NoLocalWork'
    END,
    ComparisonReason = CASE
        WHEN lad.FacultyPublicationDecisionId IS NOT NULL THEN 'Academic Online returned this publication, and our app has a current accepted publication decision for the same faculty/work.'
        WHEN lc.FacultyPublicationCandidateId IS NOT NULL THEN 'Academic Online returned this publication, and our app has a faculty-publication candidate, but it is not currently accepted.'
        WHEN lw.WorkId IS NOT NULL THEN 'Academic Online returned this publication, and the OpenAlex work exists locally, but our app has not matched it to this faculty as a publication candidate.'
        ELSE 'Academic Online returned this publication, but no local OpenAlex work was found by OpenAlex ID, DOI, or normalized title/year.'
    END
FROM AcademicOnlinePublications a
OUTER APPLY
(
    SELECT TOP (1)
        w.WorkId,
        w.OpenAlexId,
        w.Title,
        MatchMethod = CASE
            WHEN a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) THEN 'ExactOpenAlexIdMatch'
            WHEN a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')) THEN 'ExactDoiMatch'
            WHEN a.PublicationYear = w.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80) THEN 'LikelyTitleYearMatch'
            ELSE 'PossibleMatchNeedsReview'
        END
    FROM openalex.Work w
    WHERE
        (a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')))
        OR (a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')))
        OR (a.PublicationYear = w.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80))
    ORDER BY
        CASE
            WHEN a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) THEN 1
            WHEN a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')) THEN 2
            ELSE 3
        END,
        w.WorkId
) lw
OUTER APPLY
(
    SELECT TOP (1)
        pc.FacultyPublicationCandidateId,
        pc.CandidateStatus,
        pc.MatchScore,
        MatchMethod = CASE
            WHEN a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) THEN 'ExactOpenAlexIdMatch'
            WHEN a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')) THEN 'ExactDoiMatch'
            WHEN a.PublicationYear = pc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80) THEN 'LikelyTitleYearMatch'
            ELSE 'PossibleMatchNeedsReview'
        END
    FROM openalex.FacultyPublicationCandidate pc
    WHERE pc.FacultyId = a.FacultyId
      AND
      (
          (lw.WorkId IS NOT NULL AND pc.WorkId = lw.WorkId)
          OR (a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')))
          OR (a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')))
          OR (a.PublicationYear = pc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80))
      )
    ORDER BY
        CASE
            WHEN lw.WorkId IS NOT NULL AND pc.WorkId = lw.WorkId THEN 1
            WHEN a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) THEN 2
            WHEN a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')) THEN 3
            ELSE 4
        END,
        pc.MatchScore DESC,
        pc.FacultyPublicationCandidateId
) lc
OUTER APPLY
(
    SELECT TOP (1)
        d.FacultyPublicationDecisionId,
        d.DecisionStatus
    FROM openalex.FacultyPublicationDecision d
    WHERE d.FacultyId = a.FacultyId
      AND d.IsCurrent = 1
      AND d.DecisionStatus = 'Accepted'
      AND
      (
          (lc.FacultyPublicationCandidateId IS NOT NULL AND d.FacultyPublicationCandidateId = lc.FacultyPublicationCandidateId)
          OR (lw.WorkId IS NOT NULL AND d.WorkId = lw.WorkId)
      )
    ORDER BY d.ReviewedAtUtc DESC, d.FacultyPublicationDecisionId DESC
) lad;

PRINT '3) Per-faculty Academic Online comparison summary';
SELECT
    FacultyId,
    FacultyDisplayName,
    AcademicOnlineRows = COUNT(*),
    AcademicOnlineVerifiedQueryRows = SUM(CASE WHEN IsVerifiedQuery = 1 THEN 1 ELSE 0 END),
    AcademicOnlineMarkedVerifiedRows = SUM(CASE WHEN AcademicOnlineVerified = 1 THEN 1 ELSE 0 END),
    WithAcademicOpenAlexId = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(AcademicOnlineOpenAlexId)), '') IS NOT NULL THEN 1 ELSE 0 END),
    WithAcademicDoi = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(AcademicOnlineDoi)), '') IS NOT NULL THEN 1 ELSE 0 END),
    LocalWorkExists = SUM(CASE WHEN LocalWorkExists = 1 THEN 1 ELSE 0 END),
    LocalCandidateExists = SUM(CASE WHEN LocalCandidateExists = 1 THEN 1 ELSE 0 END),
    LocalAcceptedByUs = SUM(CASE WHEN LocalAcceptedDecisionExists = 1 THEN 1 ELSE 0 END),
    AcademicOnlineOnly_NoLocalWork = SUM(CASE WHEN ComparisonStatus = 'AcademicOnlineOnly_NoLocalWork' THEN 1 ELSE 0 END),
    AcademicOnlineWorkExists_NoCandidate = SUM(CASE WHEN ComparisonStatus = 'AcademicOnlineWorkExists_NoCandidate' THEN 1 ELSE 0 END),
    AcademicOnlineCandidateExists_NotAccepted = SUM(CASE WHEN ComparisonStatus = 'AcademicOnlineCandidateExists_NotAccepted' THEN 1 ELSE 0 END),
    AcademicOnlineAcceptedByUs = SUM(CASE WHEN ComparisonStatus = 'AcademicOnlineAcceptedByUs' THEN 1 ELSE 0 END)
FROM #AcademicOnlineComparison
GROUP BY FacultyId, FacultyDisplayName
ORDER BY FacultyDisplayName;

PRINT '4) Academic Online publications compared to local Work, Candidate, and Accepted Decision';
SELECT TOP (300)
    FacultyId,
    FacultyDisplayName,
    IsVerifiedQuery,
    AcademicOnlinePublicationId,
    AcademicOnlineTitle,
    AcademicOnlineYear,
    AcademicOnlineDoi,
    AcademicOnlineOpenAlexId,
    AcademicOnlineVerified,
    LocalWorkExists,
    LocalWorkId,
    LocalWorkOpenAlexId,
    LocalWorkTitle,
    LocalCandidateExists,
    LocalCandidateId,
    LocalCandidateStatus,
    LocalMatchScore,
    LocalAcceptedDecisionExists,
    LocalDecisionStatus,
    MatchMethod,
    ComparisonStatus,
    ComparisonReason
FROM #AcademicOnlineComparison
ORDER BY FacultyDisplayName, IsVerifiedQuery DESC, AcademicOnlineYear DESC, AcademicOnlineTitle;

PRINT '5) Comparison status counts';
SELECT
    ComparisonStatus,
    MatchMethod,
    RowCount = COUNT(*)
FROM #AcademicOnlineComparison
GROUP BY ComparisonStatus, MatchMethod
ORDER BY ComparisonStatus, MatchMethod;

CREATE TABLE #LocalOpenAlexOnly
(
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NULL,
    FacultyPublicationCandidateId BIGINT NOT NULL,
    WorkId BIGINT NULL,
    WorkOpenAlexId NVARCHAR(128) NULL,
    WorkTitle NVARCHAR(MAX) NULL,
    PublicationYear INT NULL,
    WorkDoi NVARCHAR(512) NULL,
    CandidateStatus NVARCHAR(100) NULL,
    DecisionStatus NVARCHAR(100) NULL,
    MatchScore DECIMAL(18,4) NULL,
    ComparisonStatus VARCHAR(100) NOT NULL,
    ComparisonReason NVARCHAR(1000) NOT NULL
);

;WITH AcademicKeys AS
(
    SELECT DISTINCT
        FacultyId,
        NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(AcademicOnlineOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(AcademicOnlineDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedAcademicTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(AcademicOnlineTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')),
        AcademicOnlineYear
    FROM #AcademicOnlineComparison
),
LocalCandidates AS
(
    SELECT
        pc.FacultyPublicationCandidateId,
        pc.FacultyId,
        rf.FacultyDisplayName,
        pc.WorkId,
        pc.WorkOpenAlexId,
        pc.WorkTitle,
        pc.PublicationYear,
        pc.WorkDoi,
        pc.CandidateStatus,
        pc.MatchScore,
        DecisionStatus = d.DecisionStatus,
        NormalizedLocalOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedLocalDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedLocalTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''))
    FROM openalex.FacultyPublicationCandidate pc
    JOIN #RunFaculty rf
        ON pc.FacultyId = rf.FacultyId
    LEFT JOIN openalex.FacultyPublicationDecision d
        ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
       AND d.IsCurrent = 1
)
INSERT INTO #LocalOpenAlexOnly
(
    FacultyId,
    FacultyDisplayName,
    FacultyPublicationCandidateId,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    PublicationYear,
    WorkDoi,
    CandidateStatus,
    DecisionStatus,
    MatchScore,
    ComparisonStatus,
    ComparisonReason
)
SELECT
    lc.FacultyId,
    lc.FacultyDisplayName,
    lc.FacultyPublicationCandidateId,
    lc.WorkId,
    lc.WorkOpenAlexId,
    lc.WorkTitle,
    lc.PublicationYear,
    lc.WorkDoi,
    lc.CandidateStatus,
    lc.DecisionStatus,
    lc.MatchScore,
    'LocalOpenAlexOnly',
    'This local faculty-publication candidate was not found in the selected Academic Online snapshot by OpenAlex ID, DOI, or normalized title/year.'
FROM LocalCandidates lc
LEFT JOIN AcademicKeys a
    ON a.FacultyId = lc.FacultyId
   AND
   (
        (a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = lc.NormalizedLocalOpenAlexId)
        OR (a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = lc.NormalizedLocalDoi)
        OR (a.AcademicOnlineYear = lc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(lc.NormalizedLocalTitle, 80))
   )
WHERE a.FacultyId IS NULL;

PRINT '6) Local candidates not seen in Academic Online snapshot, restricted to this run faculty only';
SELECT TOP (300)
    FacultyId,
    FacultyDisplayName,
    FacultyPublicationCandidateId,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    PublicationYear,
    WorkDoi,
    CandidateStatus,
    DecisionStatus,
    MatchScore,
    ComparisonStatus,
    ComparisonReason
FROM #LocalOpenAlexOnly
ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;

PRINT '7) LocalOpenAlexOnly counts by faculty, restricted to this run faculty only';
SELECT
    FacultyId,
    FacultyDisplayName,
    LocalOpenAlexOnlyCount = COUNT(*),
    LocalAcceptedOnlyCount = SUM(CASE WHEN DecisionStatus = 'Accepted' THEN 1 ELSE 0 END),
    LocalPendingOnlyCount = SUM(CASE WHEN ISNULL(DecisionStatus, '') <> 'Accepted' THEN 1 ELSE 0 END)
FROM #LocalOpenAlexOnly
GROUP BY FacultyId, FacultyDisplayName
ORDER BY FacultyDisplayName;
