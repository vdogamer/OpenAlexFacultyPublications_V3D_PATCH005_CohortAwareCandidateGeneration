/*
V3E PATCH017 - Publication Data Provenance and Responsibility Audit
SQL Server 2016-safe.

Purpose:
  Show the recorded source for each stage of the faculty-publication workflow:
    1. Publication metadata source
    2. Faculty-author identity candidate and decision source
    3. Faculty-publication candidate source
    4. Academic Online observation/verification
    5. Final publication decision source

This script reports evidence already recorded by the application. It does not assign
contractual ownership or change matching scores, candidates, or decisions.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationCandidate.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.Faculty.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID('tempdb..#PublicationProvenance') IS NOT NULL
    DROP TABLE #PublicationProvenance;

CREATE TABLE #PublicationProvenance
(
    FacultyPublicationCandidateId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NOT NULL,
    DepartmentName NVARCHAR(512) NULL,
    WorkId BIGINT NULL,
    WorkOpenAlexId NVARCHAR(128) NULL,
    WorkTitle NVARCHAR(MAX) NULL,
    WorkDoi NVARCHAR(512) NULL,
    PublicationYear INT NULL,
    AuthorDisplayName NVARCHAR(512) NULL,
    AuthorOpenAlexId NVARCHAR(128) NULL,
    MatchScore DECIMAL(18,4) NULL,
    MetadataSource VARCHAR(100) NOT NULL,
    IdentityCandidateSource VARCHAR(100) NULL,
    IdentityDecisionSource VARCHAR(100) NULL,
    IdentityDecisionStatus VARCHAR(100) NULL,
    IdentityReviewedBy NVARCHAR(256) NULL,
    PublicationCandidateSource VARCHAR(100) NULL,
    PublicationCandidateStatus VARCHAR(100) NULL,
    PublicationDecisionSource VARCHAR(100) NULL,
    PublicationDecisionStatus VARCHAR(100) NULL,
    PublicationReviewedBy NVARCHAR(256) NULL,
    AcademicOnlineObserved BIT NOT NULL,
    AcademicOnlineVerified BIT NULL,
    AcademicOnlineComparisonRunId BIGINT NULL,
    AcademicOnlineAuthorId NVARCHAR(128) NULL,
    AcademicOnlineScholarName NVARCHAR(512) NULL,
    AcademicOnlineMatchMethod VARCHAR(50) NULL
);

INSERT INTO #PublicationProvenance
(
    FacultyPublicationCandidateId,
    FacultyId,
    FacultyDisplayName,
    DepartmentName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    AuthorDisplayName,
    AuthorOpenAlexId,
    MatchScore,
    MetadataSource,
    IdentityCandidateSource,
    IdentityDecisionSource,
    IdentityDecisionStatus,
    IdentityReviewedBy,
    PublicationCandidateSource,
    PublicationCandidateStatus,
    PublicationDecisionSource,
    PublicationDecisionStatus,
    PublicationReviewedBy,
    AcademicOnlineObserved,
    AcademicOnlineVerified,
    AcademicOnlineComparisonRunId,
    AcademicOnlineAuthorId,
    AcademicOnlineScholarName,
    AcademicOnlineMatchMethod
)
SELECT
    pc.FacultyPublicationCandidateId,
    pc.FacultyId,
    f.DisplayName,
    f.DepartmentName,
    pc.WorkId,
    pc.WorkOpenAlexId,
    pc.WorkTitle,
    pc.WorkDoi,
    pc.PublicationYear,
    pc.AuthorDisplayName,
    pc.AuthorOpenAlexId,
    pc.MatchScore,
    'OpenAlex',
    famc.CandidateSource,
    famd.DecisionSource,
    famd.DecisionStatus,
    famd.ReviewedBy,
    pc.CandidateSource,
    pc.CandidateStatus,
    d.DecisionSource,
    d.DecisionStatus,
    d.ReviewedBy,
    0,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL
FROM openalex.FacultyPublicationCandidate pc
JOIN openalex.Faculty f
    ON pc.FacultyId = f.FacultyId
LEFT JOIN openalex.FacultyAuthorMatchDecision famd
    ON pc.FacultyAuthorMatchDecisionId = famd.FacultyAuthorMatchDecisionId
LEFT JOIN openalex.FacultyAuthorMatchCandidate famc
    ON famd.FacultyAuthorMatchCandidateId = famc.FacultyAuthorMatchCandidateId
LEFT JOIN openalex.FacultyPublicationDecision d
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
   AND d.IsCurrent = 1;

IF OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U') IS NOT NULL
BEGIN
    UPDATE target
    SET
        target.MetadataSource = CASE WHEN ao.AcademicOnlinePublicationSnapshotId IS NULL THEN 'OpenAlex' ELSE 'OpenAlex + Academic Online observation' END,
        target.AcademicOnlineObserved = CASE WHEN ao.AcademicOnlinePublicationSnapshotId IS NULL THEN 0 ELSE 1 END,
        target.AcademicOnlineVerified = ao.Verified,
        target.AcademicOnlineComparisonRunId = ao.AcademicOnlineComparisonRunId,
        target.AcademicOnlineAuthorId = ao.AcademicOnlineAuthorId,
        target.AcademicOnlineScholarName = ao.AcademicOnlineScholarName,
        target.AcademicOnlineMatchMethod = ao.MatchMethod
    FROM #PublicationProvenance target
    OUTER APPLY
    (
        SELECT TOP (1)
            p.AcademicOnlinePublicationSnapshotId,
            p.Verified,
            p.AcademicOnlineComparisonRunId,
            p.AcademicOnlineAuthorId,
            s.AcademicOnlineName AS AcademicOnlineScholarName,
            MatchMethod = CASE
                WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NOT NULL
                 AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                     = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(target.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                    THEN 'ExactOpenAlexIdMatch'
                WHEN NULLIF(LTRIM(RTRIM(p.Doi)), '') IS NOT NULL
                 AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(p.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                     = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(target.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
                    THEN 'ExactDoiMatch'
                ELSE 'LikelyTitleYearMatch'
            END
        FROM openalex.AcademicOnlinePublicationSnapshot p
        LEFT JOIN openalex.AcademicOnlineScholarSnapshot s
            ON s.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
           AND s.FacultyId = p.FacultyId
           AND
           (
               s.AcademicOnlineAuthorId = p.AcademicOnlineAuthorId
               OR s.AcademicOnlineAuthorId IS NULL
           )
        WHERE p.FacultyId = target.FacultyId
          AND
          (
              (
                  NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NOT NULL
                  AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                      = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(target.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
              )
              OR
              (
                  NULLIF(LTRIM(RTRIM(p.Doi)), '') IS NOT NULL
                  AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(p.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                      = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(target.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
              )
              OR
              (
                  p.PublicationYear = target.PublicationYear
                  AND LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
                      = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(target.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
              )
          )
        ORDER BY
            p.AcademicOnlineComparisonRunId DESC,
            p.IsVerifiedQuery DESC,
            p.AcademicOnlinePublicationSnapshotId DESC
    ) ao;
END;

PRINT '1) Provenance coverage summary';
SELECT
    TotalPublicationCandidates = COUNT(*),
    WithIdentityCandidateSource = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(IdentityCandidateSource)), '') IS NOT NULL THEN 1 ELSE 0 END),
    WithIdentityDecisionSource = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(IdentityDecisionSource)), '') IS NOT NULL THEN 1 ELSE 0 END),
    WithPublicationCandidateSource = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(PublicationCandidateSource)), '') IS NOT NULL THEN 1 ELSE 0 END),
    WithPublicationDecision = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(PublicationDecisionStatus)), '') IS NOT NULL THEN 1 ELSE 0 END),
    AcademicOnlineObserved = SUM(CASE WHEN AcademicOnlineObserved = 1 THEN 1 ELSE 0 END),
    AcademicOnlineVerified = SUM(CASE WHEN AcademicOnlineVerified = 1 THEN 1 ELSE 0 END),
    UnknownIdentitySource = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(IdentityCandidateSource)), '') IS NULL AND NULLIF(LTRIM(RTRIM(IdentityDecisionSource)), '') IS NULL THEN 1 ELSE 0 END)
FROM #PublicationProvenance;

PRINT '2) Recorded responsibility-source combinations';
SELECT
    MetadataSource,
    IdentityCandidateSource = ISNULL(IdentityCandidateSource, '(unknown)'),
    IdentityDecisionSource = ISNULL(IdentityDecisionSource, '(unknown)'),
    PublicationCandidateSource = ISNULL(PublicationCandidateSource, '(unknown)'),
    PublicationDecisionSource = ISNULL(PublicationDecisionSource, '(pending)'),
    AcademicOnlineObserved,
    ProvenanceRowCount = COUNT(*)
FROM #PublicationProvenance
GROUP BY
    MetadataSource,
    IdentityCandidateSource,
    IdentityDecisionSource,
    PublicationCandidateSource,
    PublicationDecisionSource,
    AcademicOnlineObserved
ORDER BY ProvenanceRowCount DESC;

PRINT '3) Detailed publication provenance rows';
SELECT TOP (300)
    FacultyPublicationCandidateId,
    FacultyId,
    FacultyDisplayName,
    DepartmentName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    AuthorDisplayName,
    AuthorOpenAlexId,
    MatchScore,
    MetadataSource,
    IdentityCandidateSource,
    IdentityDecisionSource,
    IdentityDecisionStatus,
    IdentityReviewedBy,
    PublicationCandidateSource,
    PublicationCandidateStatus,
    PublicationDecisionSource,
    PublicationDecisionStatus,
    PublicationReviewedBy,
    AcademicOnlineObserved,
    AcademicOnlineVerified,
    AcademicOnlineComparisonRunId,
    AcademicOnlineAuthorId,
    AcademicOnlineScholarName,
    AcademicOnlineMatchMethod
FROM #PublicationProvenance
ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;

PRINT '4) Rows requiring role clarification';
SELECT TOP (300)
    FacultyPublicationCandidateId,
    FacultyDisplayName,
    WorkTitle,
    PublicationYear,
    IdentityCandidateSource,
    IdentityDecisionSource,
    PublicationCandidateSource,
    PublicationDecisionSource,
    PublicationDecisionStatus,
    AcademicOnlineObserved,
    AcademicOnlineVerified,
    ClarificationReason = CASE
        WHEN IdentityCandidateSource IS NULL AND IdentityDecisionSource IS NULL THEN 'Identity matching source is not recorded.'
        WHEN PublicationCandidateSource IS NULL THEN 'Publication matching source is not recorded.'
        WHEN PublicationDecisionStatus IS NULL THEN 'Final UVA publication review is pending.'
        WHEN AcademicOnlineObserved = 0 THEN 'No matching Academic Online snapshot was observed.'
        ELSE 'Review source responsibilities.'
    END
FROM #PublicationProvenance
WHERE
    (IdentityCandidateSource IS NULL AND IdentityDecisionSource IS NULL)
    OR PublicationCandidateSource IS NULL
    OR PublicationDecisionStatus IS NULL
    OR AcademicOnlineObserved = 0
ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;
