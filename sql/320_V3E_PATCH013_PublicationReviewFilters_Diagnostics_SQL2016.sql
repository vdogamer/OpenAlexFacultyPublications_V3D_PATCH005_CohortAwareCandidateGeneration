/*
V3E PATCH013 - Publication Review Filters Diagnostics
SQL Server 2016-safe.

Purpose:
  Validate the same filter concepts used by /Review/Publications.

Change the variables below to test different filter combinations.
*/

SET NOCOUNT ON;

DECLARE @SearchText NVARCHAR(200) = NULL;
DECLARE @MinimumScore DECIMAL(10,4) = NULL;
DECLARE @InstitutionEvidenceFilter VARCHAR(20) = 'Any'; -- Any, Present, Missing
DECLARE @CoauthorEvidenceFilter VARCHAR(20) = 'Any'; -- Any, Present, Missing
DECLARE @NearDuplicateFilter VARCHAR(20) = 'Any'; -- Any, Warning, Resolved, None
DECLARE @ResolutionStatusFilter VARCHAR(50) = 'Any'; -- Any, Resolved, Unresolved, DuplicateOfCandidate, KeepSeparate, SuppressWarning

DECLARE @SearchTextLike NVARCHAR(210) = NULL;
IF NULLIF(LTRIM(RTRIM(@SearchText)), N'') IS NOT NULL
BEGIN
    SET @SearchTextLike = N'%' + LTRIM(RTRIM(@SearchText)) + N'%';
END;

IF OBJECT_ID(N'openalex.vw_FacultyPublicationReviewQueue', N'V') IS NULL
BEGIN
    SELECT 'Missing openalex.vw_FacultyPublicationReviewQueue.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationCandidate.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationNearDuplicateResolution. Run PATCH012 setup first.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Filtered publication review cards';
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        q.FacultyDisplayName,
        q.PrimaryEmail,
        q.DepartmentName,
        q.WorkTitle,
        q.PublicationYear,
        q.WorkOpenAlexId,
        q.AuthorDisplayName,
        q.CandidateStatus,
        q.MatchScore,
        q.EvidenceSummary,
        q.CreatedAtUtc,
        pc.FacultyId,
        pc.WorkId,
        NearDuplicateTitleKey = LEFT(
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    LTRIM(RTRIM(ISNULL(q.WorkTitle, ''))),
                    ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''), '/', ''), CHAR(39), ''), '  ', ' ')
            ),
            90
        ),
        DuplicateCandidateCount = COUNT(*) OVER
        (
            PARTITION BY
                pc.FacultyId,
                COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle)
        ),
        ReviewGroupRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                pc.FacultyId,
                COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle)
            ORDER BY
                ISNULL(q.MatchScore, 0) DESC,
                q.CreatedAtUtc DESC,
                q.FacultyPublicationCandidateId DESC
        )
    FROM openalex.vw_FacultyPublicationReviewQueue q
    JOIN openalex.FacultyPublicationCandidate pc
        ON q.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
    WHERE
        (
            q.CandidateStatus IS NULL
            OR q.CandidateStatus IN ('PendingReview', 'NeedsMoreEvidence')
        )
        AND NOT EXISTS
        (
            SELECT 1
            FROM openalex.FacultyPublicationDecision d
            WHERE d.FacultyPublicationCandidateId = q.FacultyPublicationCandidateId
              AND d.IsCurrent = 1
              AND d.DecisionStatus IN ('Accepted', 'Rejected')
        )
),
VisibleCandidates AS
(
    SELECT *
    FROM PendingCandidates
    WHERE ReviewGroupRank = 1
),
VisibleCandidatesWithNearDuplicates AS
(
    SELECT
        vc.*,
        NearDuplicateCandidateCount = COUNT(*) OVER
        (
            PARTITION BY
                vc.FacultyId,
                ISNULL(vc.PublicationYear, -1),
                ISNULL(vc.AuthorDisplayName, ''),
                vc.NearDuplicateTitleKey
        )
    FROM VisibleCandidates vc
)
SELECT TOP (100)
    vc.FacultyPublicationCandidateId,
    vc.FacultyDisplayName,
    vc.DepartmentName,
    vc.WorkTitle,
    vc.PublicationYear,
    vc.AuthorDisplayName,
    vc.CandidateStatus,
    vc.MatchScore,
    InstitutionEvidencePresent = CASE WHEN ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%' THEN 1 ELSE 0 END,
    CoauthorEvidencePresent = CASE WHEN ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%' THEN 1 ELSE 0 END,
    NearDuplicateCandidateCount = vc.NearDuplicateCandidateCount,
    ResolutionStatus = res.ResolutionStatus,
    vc.CreatedAtUtc
FROM VisibleCandidatesWithNearDuplicates vc
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = vc.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE
    (
        @SearchTextLike IS NULL
        OR vc.FacultyDisplayName LIKE @SearchTextLike
        OR vc.DepartmentName LIKE @SearchTextLike
        OR vc.WorkTitle LIKE @SearchTextLike
        OR vc.AuthorDisplayName LIKE @SearchTextLike
        OR vc.WorkOpenAlexId LIKE @SearchTextLike
    )
    AND (@MinimumScore IS NULL OR vc.MatchScore >= @MinimumScore)
    AND
    (
        @InstitutionEvidenceFilter = 'Any'
        OR (@InstitutionEvidenceFilter = 'Present' AND ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%')
        OR (@InstitutionEvidenceFilter = 'Missing' AND (ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=%' OR ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=0%'))
    )
    AND
    (
        @CoauthorEvidenceFilter = 'Any'
        OR (@CoauthorEvidenceFilter = 'Present' AND ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%')
        OR (@CoauthorEvidenceFilter = 'Missing' AND (ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=%' OR ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=0%'))
    )
    AND
    (
        @NearDuplicateFilter = 'Any'
        OR (@NearDuplicateFilter = 'Warning' AND vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL)
        OR (@NearDuplicateFilter = 'Resolved' AND res.ResolutionStatus IS NOT NULL)
        OR (@NearDuplicateFilter = 'None' AND vc.NearDuplicateCandidateCount <= 1)
    )
    AND
    (
        @ResolutionStatusFilter = 'Any'
        OR (@ResolutionStatusFilter = 'Unresolved' AND vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL)
        OR (@ResolutionStatusFilter = 'Resolved' AND res.ResolutionStatus IS NOT NULL)
        OR res.ResolutionStatus = @ResolutionStatusFilter
    )
ORDER BY vc.CreatedAtUtc DESC;

PRINT '2) Filtered result count';
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        q.FacultyDisplayName,
        q.DepartmentName,
        q.WorkTitle,
        q.PublicationYear,
        q.WorkOpenAlexId,
        q.AuthorDisplayName,
        q.CandidateStatus,
        q.MatchScore,
        q.EvidenceSummary,
        q.CreatedAtUtc,
        pc.FacultyId,
        pc.WorkId,
        NearDuplicateTitleKey = LEFT(
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    LTRIM(RTRIM(ISNULL(q.WorkTitle, ''))),
                    ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''), '/', ''), CHAR(39), ''), '  ', ' ')
            ),
            90
        ),
        ReviewGroupRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                pc.FacultyId,
                COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle)
            ORDER BY
                ISNULL(q.MatchScore, 0) DESC,
                q.CreatedAtUtc DESC,
                q.FacultyPublicationCandidateId DESC
        )
    FROM openalex.vw_FacultyPublicationReviewQueue q
    JOIN openalex.FacultyPublicationCandidate pc
        ON q.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
    WHERE
        (
            q.CandidateStatus IS NULL
            OR q.CandidateStatus IN ('PendingReview', 'NeedsMoreEvidence')
        )
        AND NOT EXISTS
        (
            SELECT 1
            FROM openalex.FacultyPublicationDecision d
            WHERE d.FacultyPublicationCandidateId = q.FacultyPublicationCandidateId
              AND d.IsCurrent = 1
              AND d.DecisionStatus IN ('Accepted', 'Rejected')
        )
),
VisibleCandidates AS
(
    SELECT *
    FROM PendingCandidates
    WHERE ReviewGroupRank = 1
),
VisibleCandidatesWithNearDuplicates AS
(
    SELECT
        vc.*,
        NearDuplicateCandidateCount = COUNT(*) OVER
        (
            PARTITION BY
                vc.FacultyId,
                ISNULL(vc.PublicationYear, -1),
                ISNULL(vc.AuthorDisplayName, ''),
                vc.NearDuplicateTitleKey
        )
    FROM VisibleCandidates vc
)
SELECT
    FilteredReviewCardCount = COUNT(*)
FROM VisibleCandidatesWithNearDuplicates vc
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = vc.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE
    (
        @SearchTextLike IS NULL
        OR vc.FacultyDisplayName LIKE @SearchTextLike
        OR vc.DepartmentName LIKE @SearchTextLike
        OR vc.WorkTitle LIKE @SearchTextLike
        OR vc.AuthorDisplayName LIKE @SearchTextLike
        OR vc.WorkOpenAlexId LIKE @SearchTextLike
    )
    AND (@MinimumScore IS NULL OR vc.MatchScore >= @MinimumScore)
    AND
    (
        @InstitutionEvidenceFilter = 'Any'
        OR (@InstitutionEvidenceFilter = 'Present' AND ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%')
        OR (@InstitutionEvidenceFilter = 'Missing' AND (ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=%' OR ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=0%'))
    )
    AND
    (
        @CoauthorEvidenceFilter = 'Any'
        OR (@CoauthorEvidenceFilter = 'Present' AND ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%')
        OR (@CoauthorEvidenceFilter = 'Missing' AND (ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=%' OR ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=0%'))
    )
    AND
    (
        @NearDuplicateFilter = 'Any'
        OR (@NearDuplicateFilter = 'Warning' AND vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL)
        OR (@NearDuplicateFilter = 'Resolved' AND res.ResolutionStatus IS NOT NULL)
        OR (@NearDuplicateFilter = 'None' AND vc.NearDuplicateCandidateCount <= 1)
    )
    AND
    (
        @ResolutionStatusFilter = 'Any'
        OR (@ResolutionStatusFilter = 'Unresolved' AND vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL)
        OR (@ResolutionStatusFilter = 'Resolved' AND res.ResolutionStatus IS NOT NULL)
        OR res.ResolutionStatus = @ResolutionStatusFilter
    );
