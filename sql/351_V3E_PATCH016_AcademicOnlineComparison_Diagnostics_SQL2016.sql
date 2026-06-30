/*
V3E PATCH016 - Academic Online Source Comparison Diagnostics
SQL Server 2016-safe.

Purpose:
  Compare Academic Online publication snapshots against local OpenAlex publication candidates
  and accepted publication decisions.

Usage:
  1. Run sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql
  2. Run the worker job AcademicOnlineComparison
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

PRINT '2) Scholar snapshots';
SELECT
    s.AcademicOnlineScholarSnapshotId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.DepartmentName,
    s.AcademicOnlineAuthorId,
    s.AcademicOnlineName,
    s.AcademicOnlineEmail,
    s.AcademicOnlinePosition,
    s.AcademicOnlineOrcidUrl,
    s.PublicationCount,
    s.HasVerifiedPublications,
    s.SearchRankingScore,
    AcademicOnlineScholarFound = CASE WHEN s.AcademicOnlineAuthorId IS NULL THEN 0 ELSE 1 END
FROM openalex.AcademicOnlineScholarSnapshot s
WHERE s.AcademicOnlineComparisonRunId = @RunId
  AND (@FacultyId IS NULL OR s.FacultyId = @FacultyId)
ORDER BY s.FacultyDisplayName;

PRINT '3) Academic Online publication snapshot counts';
SELECT
    p.FacultyId,
    s.FacultyDisplayName,
    p.AcademicOnlineAuthorId,
    p.IsVerifiedQuery,
    PublicationRows = COUNT(*),
    WithOpenAlexId = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NOT NULL THEN 1 ELSE 0 END),
    WithDoi = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(p.Doi)), '') IS NOT NULL THEN 1 ELSE 0 END),
    MarkedVerified = SUM(CASE WHEN p.Verified = 1 THEN 1 ELSE 0 END)
FROM openalex.AcademicOnlinePublicationSnapshot p
LEFT JOIN openalex.AcademicOnlineScholarSnapshot s
    ON p.AcademicOnlineComparisonRunId = s.AcademicOnlineComparisonRunId
   AND p.FacultyId = s.FacultyId
   AND p.AcademicOnlineAuthorId = s.AcademicOnlineAuthorId
WHERE p.AcademicOnlineComparisonRunId = @RunId
  AND (@FacultyId IS NULL OR p.FacultyId = @FacultyId)
GROUP BY p.FacultyId, s.FacultyDisplayName, p.AcademicOnlineAuthorId, p.IsVerifiedQuery
ORDER BY s.FacultyDisplayName, p.IsVerifiedQuery;

PRINT '4) Academic Online publications compared to local OpenAlex candidates/decisions';
;WITH AcademicOnlinePublications AS
(
    SELECT
        p.*,
        NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedAcademicTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''))
    FROM openalex.AcademicOnlinePublicationSnapshot p
    WHERE p.AcademicOnlineComparisonRunId = @RunId
      AND (@FacultyId IS NULL OR p.FacultyId = @FacultyId)
),
LocalCandidates AS
(
    SELECT
        pc.FacultyPublicationCandidateId,
        pc.FacultyId,
        pc.WorkId,
        pc.WorkOpenAlexId,
        pc.WorkTitle,
        pc.WorkDoi,
        pc.PublicationYear,
        pc.AuthorDisplayName,
        pc.CandidateStatus,
        pc.MatchScore,
        DecisionStatus = d.DecisionStatus,
        ReviewedBy = d.ReviewedBy,
        ReviewedAtUtc = d.ReviewedAtUtc,
        NormalizedLocalOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedLocalDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedLocalTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''))
    FROM openalex.FacultyPublicationCandidate pc
    LEFT JOIN openalex.FacultyPublicationDecision d
        ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
       AND d.IsCurrent = 1
    WHERE (@FacultyId IS NULL OR pc.FacultyId = @FacultyId)
)
SELECT TOP (200)
    a.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    a.IsVerifiedQuery,
    AcademicOnlinePublicationId = a.AcademicOnlinePublicationId,
    AcademicOnlineTitle = a.Title,
    AcademicOnlineYear = a.PublicationYear,
    AcademicOnlineDoi = a.Doi,
    AcademicOnlineOpenAlexId = a.OpenAlexId,
    AcademicOnlineVerified = a.Verified,
    LocalCandidateId = lc.FacultyPublicationCandidateId,
    LocalWorkId = lc.WorkId,
    LocalWorkOpenAlexId = lc.WorkOpenAlexId,
    LocalWorkTitle = lc.WorkTitle,
    LocalDecisionStatus = lc.DecisionStatus,
    LocalMatchScore = lc.MatchScore,
    ComparisonStatus = CASE
        WHEN lc.FacultyPublicationCandidateId IS NULL THEN 'AcademicOnlineOnly'
        WHEN a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = lc.NormalizedLocalOpenAlexId THEN 'ExactOpenAlexIdMatch'
        WHEN a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = lc.NormalizedLocalDoi THEN 'ExactDoiMatch'
        WHEN a.PublicationYear = lc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(lc.NormalizedLocalTitle, 80) THEN 'LikelyTitleYearMatch'
        ELSE 'PossibleMatchNeedsReview'
    END,
    ComparisonReason = CASE
        WHEN lc.FacultyPublicationCandidateId IS NULL THEN 'Academic Online returned this publication, but no local candidate matched by OpenAlex ID, DOI, or normalized title/year.'
        WHEN a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = lc.NormalizedLocalOpenAlexId THEN 'Academic Online and our local OpenAlex candidate share the same OpenAlex work ID.'
        WHEN a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = lc.NormalizedLocalDoi THEN 'Academic Online and our local candidate share the same DOI.'
        WHEN a.PublicationYear = lc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(lc.NormalizedLocalTitle, 80) THEN 'Academic Online and our local candidate have a similar normalized title and the same year.'
        ELSE 'Potential relationship found, but it should be reviewed manually.'
    END
FROM AcademicOnlinePublications a
LEFT JOIN LocalCandidates lc
    ON lc.FacultyId = a.FacultyId
   AND
   (
        (a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = lc.NormalizedLocalOpenAlexId)
        OR (a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = lc.NormalizedLocalDoi)
        OR (a.PublicationYear = lc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(lc.NormalizedLocalTitle, 80))
   )
LEFT JOIN openalex.Faculty f
    ON a.FacultyId = f.FacultyId
ORDER BY f.DisplayName, a.IsVerifiedQuery DESC, a.PublicationYear DESC, a.Title;

PRINT '5) Local candidates not seen in Academic Online snapshot';
;WITH AcademicKeys AS
(
    SELECT DISTINCT
        FacultyId,
        NormalizedAcademicOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedAcademicDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedAcademicTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')),
        PublicationYear
    FROM openalex.AcademicOnlinePublicationSnapshot
    WHERE AcademicOnlineComparisonRunId = @RunId
      AND (@FacultyId IS NULL OR FacultyId = @FacultyId)
),
LocalCandidates AS
(
    SELECT
        pc.FacultyPublicationCandidateId,
        pc.FacultyId,
        pc.WorkId,
        pc.WorkOpenAlexId,
        pc.WorkTitle,
        pc.WorkDoi,
        pc.PublicationYear,
        pc.CandidateStatus,
        pc.MatchScore,
        DecisionStatus = d.DecisionStatus,
        NormalizedLocalOpenAlexId = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')),
        NormalizedLocalDoi = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')),
        NormalizedLocalTitle = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''))
    FROM openalex.FacultyPublicationCandidate pc
    LEFT JOIN openalex.FacultyPublicationDecision d
        ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
       AND d.IsCurrent = 1
    WHERE (@FacultyId IS NULL OR pc.FacultyId = @FacultyId)
)
SELECT TOP (200)
    lc.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    lc.FacultyPublicationCandidateId,
    lc.WorkId,
    lc.WorkOpenAlexId,
    lc.WorkTitle,
    lc.PublicationYear,
    lc.WorkDoi,
    lc.CandidateStatus,
    lc.DecisionStatus,
    lc.MatchScore,
    ComparisonStatus = 'LocalOpenAlexOnly',
    ComparisonReason = 'Local OpenAlex candidate/decision was not found in the latest Academic Online snapshot by OpenAlex ID, DOI, or normalized title/year.'
FROM LocalCandidates lc
LEFT JOIN AcademicKeys a
    ON a.FacultyId = lc.FacultyId
   AND
   (
        (a.NormalizedAcademicOpenAlexId <> '' AND a.NormalizedAcademicOpenAlexId = lc.NormalizedLocalOpenAlexId)
        OR (a.NormalizedAcademicDoi <> '' AND a.NormalizedAcademicDoi = lc.NormalizedLocalDoi)
        OR (a.PublicationYear = lc.PublicationYear AND LEFT(a.NormalizedAcademicTitle, 80) = LEFT(lc.NormalizedLocalTitle, 80))
   )
LEFT JOIN openalex.Faculty f
    ON lc.FacultyId = f.FacultyId
WHERE a.FacultyId IS NULL
ORDER BY f.DisplayName, lc.PublicationYear DESC, lc.WorkTitle;
