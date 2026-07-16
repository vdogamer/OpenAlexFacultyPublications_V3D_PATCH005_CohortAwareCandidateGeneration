/*
V3E PATCH017A - Provenance Coverage Gaps and Status Consistency Diagnostics
SQL Server 2016-safe.

Purpose:
  1. Show Academic Online publications that are missing from one or more local stages.
  2. Distinguish local Work existence from faculty-publication candidate existence.
  3. Show whether UVA has a current accepted publication decision.
  4. Flag candidate status values that disagree with the current publication decision.

This script is read-only.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlinePublicationSnapshot. Run PATCH016 first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlineScholarSnapshot. Run PATCH016 first.' AS DiagnosticMessage;
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

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationDecision. Run the publication review schema first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID('tempdb..#Coverage') IS NOT NULL DROP TABLE #Coverage;

CREATE TABLE #Coverage
(
    AcademicOnlinePublicationSnapshotId BIGINT NOT NULL,
    AcademicOnlineComparisonRunId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NULL,
    AcademicOnlineScholarName NVARCHAR(512) NULL,
    AcademicOnlinePublicationId NVARCHAR(128) NULL,
    PublicationTitle NVARCHAR(MAX) NULL,
    PublicationYear INT NULL,
    Doi NVARCHAR(512) NULL,
    OpenAlexId NVARCHAR(128) NULL,
    AcademicOnlineVerified BIT NULL,
    LocalWorkId BIGINT NULL,
    LocalCandidateId BIGINT NULL,
    LocalCandidateStatus NVARCHAR(100) NULL,
    LocalMatchScore DECIMAL(18,4) NULL,
    LocalDecisionStatus NVARCHAR(100) NULL,
    LocalDecisionSource NVARCHAR(100) NULL,
    LocalReviewedBy NVARCHAR(256) NULL,
    MatchMethod VARCHAR(50) NOT NULL,
    CoverageStatus VARCHAR(100) NOT NULL,
    CoverageExplanation NVARCHAR(1000) NOT NULL
);

;WITH LatestFacultyRun AS
(
    SELECT
        FacultyId,
        AcademicOnlineComparisonRunId = MAX(AcademicOnlineComparisonRunId)
    FROM openalex.AcademicOnlinePublicationSnapshot
    GROUP BY FacultyId
),
AcademicOnlineRanked AS
(
    SELECT
        p.AcademicOnlinePublicationSnapshotId,
        p.AcademicOnlineComparisonRunId,
        p.FacultyId,
        p.AcademicOnlineAuthorId,
        p.AcademicOnlinePublicationId,
        p.Title,
        p.PublicationYear,
        p.Doi,
        p.OpenAlexId,
        p.Verified,
        s.AcademicOnlineName AS AcademicOnlineScholarName,
        DeduplicationRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                p.FacultyId,
                COALESCE
                (
                    NULLIF(UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')), ''),
                    NULLIF(LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')), ''),
                    LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 120)
                        + '|' + ISNULL(CONVERT(VARCHAR(10), p.PublicationYear), '')
                )
            ORDER BY
                p.IsVerifiedQuery DESC,
                p.Verified DESC,
                p.AcademicOnlinePublicationSnapshotId DESC
        )
    FROM openalex.AcademicOnlinePublicationSnapshot p
    JOIN LatestFacultyRun latest
        ON latest.FacultyId = p.FacultyId
       AND latest.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
    LEFT JOIN openalex.AcademicOnlineScholarSnapshot s
        ON s.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
       AND s.FacultyId = p.FacultyId
       AND
       (
           s.AcademicOnlineAuthorId = p.AcademicOnlineAuthorId
           OR s.AcademicOnlineAuthorId IS NULL
       )
)
INSERT INTO #Coverage
(
    AcademicOnlinePublicationSnapshotId,
    AcademicOnlineComparisonRunId,
    FacultyId,
    FacultyDisplayName,
    AcademicOnlineScholarName,
    AcademicOnlinePublicationId,
    PublicationTitle,
    PublicationYear,
    Doi,
    OpenAlexId,
    AcademicOnlineVerified,
    LocalWorkId,
    LocalCandidateId,
    LocalCandidateStatus,
    LocalMatchScore,
    LocalDecisionStatus,
    LocalDecisionSource,
    LocalReviewedBy,
    MatchMethod,
    CoverageStatus,
    CoverageExplanation
)
SELECT
    ao.AcademicOnlinePublicationSnapshotId,
    ao.AcademicOnlineComparisonRunId,
    ao.FacultyId,
    f.DisplayName,
    ao.AcademicOnlineScholarName,
    ao.AcademicOnlinePublicationId,
    ao.Title,
    ao.PublicationYear,
    ao.Doi,
    ao.OpenAlexId,
    ao.Verified,
    lw.WorkId,
    lc.FacultyPublicationCandidateId,
    lc.CandidateStatus,
    lc.MatchScore,
    ld.DecisionStatus,
    ld.DecisionSource,
    ld.ReviewedBy,
    ISNULL(lw.MatchMethod, ISNULL(lc.MatchMethod, 'NoLocalMatch')),
    CASE
        WHEN ld.DecisionStatus = 'Accepted' THEN 'AcademicOnlineAcceptedByUs'
        WHEN lc.FacultyPublicationCandidateId IS NOT NULL THEN 'AcademicOnlineCandidateExists_NotAccepted'
        WHEN lw.WorkId IS NOT NULL THEN 'AcademicOnlineWorkExists_NoCandidate'
        ELSE 'AcademicOnlineOnly_NoLocalWork'
    END,
    CASE
        WHEN ld.DecisionStatus = 'Accepted' THEN 'Academic Online contains this publication and UVA has a current accepted publication decision for the same faculty and work.'
        WHEN lc.FacultyPublicationCandidateId IS NOT NULL THEN 'Academic Online contains this publication and UVA has a faculty-publication candidate, but there is no current accepted decision.'
        WHEN lw.WorkId IS NOT NULL THEN 'Academic Online contains this publication and the OpenAlex work exists locally, but UVA has not created a publication candidate for this faculty.'
        ELSE 'Academic Online contains this publication, but the corresponding OpenAlex work is not present in the local database.'
    END
FROM AcademicOnlineRanked ao
JOIN openalex.Faculty f
    ON f.FacultyId = ao.FacultyId
OUTER APPLY
(
    SELECT TOP (1)
        w.WorkId,
        MatchMethod = CASE
            WHEN NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                THEN 'ExactOpenAlexIdMatch'
            WHEN NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
                THEN 'ExactDoiMatch'
            ELSE 'LikelyTitleYearMatch'
        END
    FROM openalex.Work w
    WHERE
        (
            NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
            AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
        )
        OR
        (
            NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
            AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
        )
        OR
        (
            ao.PublicationYear = w.PublicationYear
            AND LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ao.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
                = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
        )
    ORDER BY
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) THEN 1
            WHEN NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')) THEN 2
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
            WHEN NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                THEN 'ExactOpenAlexIdMatch'
            WHEN NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
                THEN 'ExactDoiMatch'
            ELSE 'LikelyTitleYearMatch'
        END
    FROM openalex.FacultyPublicationCandidate pc
    WHERE pc.FacultyId = ao.FacultyId
      AND
      (
          (lw.WorkId IS NOT NULL AND pc.WorkId = lw.WorkId)
          OR
          (
              NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
              AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                  = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
          )
          OR
          (
              NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
              AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                  = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
          )
          OR
          (
              ao.PublicationYear = pc.PublicationYear
              AND LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ao.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
                  = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
          )
      )
    ORDER BY
        CASE WHEN lw.WorkId IS NOT NULL AND pc.WorkId = lw.WorkId THEN 1 ELSE 2 END,
        pc.MatchScore DESC,
        pc.FacultyPublicationCandidateId
) lc
OUTER APPLY
(
    SELECT TOP (1)
        d.DecisionStatus,
        d.DecisionSource,
        d.ReviewedBy
    FROM openalex.FacultyPublicationDecision d
    WHERE d.FacultyPublicationCandidateId = lc.FacultyPublicationCandidateId
      AND d.IsCurrent = 1
    ORDER BY d.ReviewedAtUtc DESC, d.FacultyPublicationDecisionId DESC
) ld
WHERE ao.DeduplicationRank = 1;

PRINT '1) Academic Online coverage summary';
SELECT
    AcademicOnlinePublicationRows = COUNT(*),
    AcademicOnlineOnly_NoLocalWork = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineOnly_NoLocalWork' THEN 1 ELSE 0 END),
    AcademicOnlineWorkExists_NoCandidate = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineWorkExists_NoCandidate' THEN 1 ELSE 0 END),
    AcademicOnlineCandidateExists_NotAccepted = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineCandidateExists_NotAccepted' THEN 1 ELSE 0 END),
    AcademicOnlineAcceptedByUs = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineAcceptedByUs' THEN 1 ELSE 0 END)
FROM #Coverage;

PRINT '2) Academic Online coverage counts by faculty';
SELECT
    FacultyId,
    FacultyDisplayName,
    AcademicOnlinePublicationRows = COUNT(*),
    NoLocalWork = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineOnly_NoLocalWork' THEN 1 ELSE 0 END),
    LocalWorkNoCandidate = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineWorkExists_NoCandidate' THEN 1 ELSE 0 END),
    CandidateNotAccepted = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineCandidateExists_NotAccepted' THEN 1 ELSE 0 END),
    AcceptedByBoth = SUM(CASE WHEN CoverageStatus = 'AcademicOnlineAcceptedByUs' THEN 1 ELSE 0 END)
FROM #Coverage
GROUP BY FacultyId, FacultyDisplayName
ORDER BY FacultyDisplayName;

PRINT '3) Detailed Academic Online coverage gaps';
SELECT TOP (300)
    FacultyId,
    FacultyDisplayName,
    AcademicOnlineScholarName,
    AcademicOnlinePublicationId,
    PublicationTitle,
    PublicationYear,
    Doi,
    OpenAlexId,
    AcademicOnlineVerified,
    LocalWorkId,
    LocalCandidateId,
    LocalCandidateStatus,
    LocalMatchScore,
    LocalDecisionStatus,
    MatchMethod,
    CoverageStatus,
    CoverageExplanation
FROM #Coverage
WHERE CoverageStatus <> 'AcademicOnlineAcceptedByUs'
ORDER BY FacultyDisplayName, PublicationYear DESC, PublicationTitle;

PRINT '4) Academic Online publications accepted by both systems';
SELECT TOP (300)
    FacultyId,
    FacultyDisplayName,
    PublicationTitle,
    PublicationYear,
    OpenAlexId,
    Doi,
    LocalWorkId,
    LocalCandidateId,
    LocalCandidateStatus,
    LocalDecisionStatus,
    LocalDecisionSource,
    LocalReviewedBy,
    MatchMethod
FROM #Coverage
WHERE CoverageStatus = 'AcademicOnlineAcceptedByUs'
ORDER BY FacultyDisplayName, PublicationYear DESC, PublicationTitle;

PRINT '5) Candidate status versus current UVA decision mismatches';
SELECT TOP (300)
    pc.FacultyPublicationCandidateId,
    pc.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    pc.WorkTitle,
    pc.PublicationYear,
    pc.WorkOpenAlexId,
    pc.CandidateStatus,
    d.DecisionStatus,
    d.DecisionSource,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    ConsistencyStatus = 'CandidateDecisionMismatch',
    ConsistencyExplanation = 'The current UVA publication decision is authoritative, but the candidate status does not represent that decision.'
FROM openalex.FacultyPublicationCandidate pc
JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
JOIN openalex.FacultyPublicationDecision d
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
   AND d.IsCurrent = 1
WHERE
    (
        d.DecisionStatus = 'Accepted'
        AND ISNULL(pc.CandidateStatus, '') NOT IN ('AutoAcceptedCandidate', 'AcceptedCandidate', 'Accepted')
    )
    OR
    (
        d.DecisionStatus = 'Rejected'
        AND ISNULL(pc.CandidateStatus, '') NOT IN ('AutoRejectedCandidate', 'RejectedCandidate', 'Rejected')
    )
ORDER BY f.DisplayName, pc.PublicationYear DESC, pc.WorkTitle;

PRINT '6) Action-oriented issue counts';
SELECT IssueType, IssueCount
FROM
(
    SELECT 'Ingest missing local OpenAlex work' AS IssueType, COUNT(*) AS IssueCount
    FROM #Coverage
    WHERE CoverageStatus = 'AcademicOnlineOnly_NoLocalWork'

    UNION ALL

    SELECT 'Generate faculty-publication candidate', COUNT(*)
    FROM #Coverage
    WHERE CoverageStatus = 'AcademicOnlineWorkExists_NoCandidate'

    UNION ALL

    SELECT 'Complete UVA publication review', COUNT(*)
    FROM #Coverage
    WHERE CoverageStatus = 'AcademicOnlineCandidateExists_NotAccepted'

    UNION ALL

    SELECT 'Reconcile candidate status with current decision', COUNT(*)
    FROM openalex.FacultyPublicationCandidate pc
    JOIN openalex.FacultyPublicationDecision d
        ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
       AND d.IsCurrent = 1
    WHERE
        (
            d.DecisionStatus = 'Accepted'
            AND ISNULL(pc.CandidateStatus, '') NOT IN ('AutoAcceptedCandidate', 'AcceptedCandidate', 'Accepted')
        )
        OR
        (
            d.DecisionStatus = 'Rejected'
            AND ISNULL(pc.CandidateStatus, '') NOT IN ('AutoRejectedCandidate', 'RejectedCandidate', 'Rejected')
        )
) issues
ORDER BY IssueType;
