/*
V3E PATCH011 - Near-Duplicate Publication Detection Diagnostics
SQL Server 2016-safe.

Purpose:
  Find pending publication review cards that may represent the same real-world publication
  across different OpenAlex work records.

PATCH011 does not hide, merge, delete, accept, or reject rows.
It only flags possible near-duplicates for reviewer awareness.
*/

SET NOCOUNT ON;

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

PRINT '1) Possible near-duplicate pending publication groups';
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        q.FacultyDisplayName,
        pc.FacultyId,
        pc.WorkId,
        q.WorkOpenAlexId,
        q.WorkTitle,
        q.PublicationYear,
        q.AuthorDisplayName,
        q.CandidateStatus,
        q.MatchScore,
        q.CreatedAtUtc,
        CandidateGroupKey = COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle),
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
NearDuplicateGroups AS
(
    SELECT
        FacultyId,
        FacultyDisplayName,
        PublicationYear,
        AuthorDisplayName,
        NearDuplicateTitleKey,
        CandidateCount = COUNT(*),
        DistinctWorkCount = COUNT(DISTINCT WorkId),
        BestMatchScore = MAX(MatchScore),
        OldestCandidateCreatedAtUtc = MIN(CreatedAtUtc),
        NewestCandidateCreatedAtUtc = MAX(CreatedAtUtc)
    FROM VisibleCandidates
    GROUP BY
        FacultyId,
        FacultyDisplayName,
        PublicationYear,
        AuthorDisplayName,
        NearDuplicateTitleKey
    HAVING COUNT(*) > 1
)
SELECT
    FacultyId,
    FacultyDisplayName,
    PublicationYear,
    AuthorDisplayName,
    NearDuplicateTitleKey,
    CandidateCount,
    DistinctWorkCount,
    BestMatchScore,
    OldestCandidateCreatedAtUtc,
    NewestCandidateCreatedAtUtc
FROM NearDuplicateGroups
ORDER BY CandidateCount DESC, BestMatchScore DESC, FacultyDisplayName, PublicationYear DESC;

PRINT '2) Candidate rows inside possible near-duplicate groups';
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        q.FacultyDisplayName,
        pc.FacultyId,
        pc.WorkId,
        q.WorkOpenAlexId,
        q.WorkTitle,
        q.PublicationYear,
        q.AuthorDisplayName,
        q.CandidateStatus,
        q.MatchScore,
        q.CreatedAtUtc,
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
NearDuplicateGroups AS
(
    SELECT
        FacultyId,
        PublicationYear,
        AuthorDisplayName,
        NearDuplicateTitleKey
    FROM VisibleCandidates
    GROUP BY
        FacultyId,
        PublicationYear,
        AuthorDisplayName,
        NearDuplicateTitleKey
    HAVING COUNT(*) > 1
)
SELECT
    vc.FacultyPublicationCandidateId,
    vc.FacultyId,
    vc.FacultyDisplayName,
    vc.WorkId,
    vc.WorkOpenAlexId,
    vc.WorkTitle,
    vc.PublicationYear,
    vc.AuthorDisplayName,
    vc.CandidateStatus,
    vc.MatchScore,
    vc.NearDuplicateTitleKey,
    vc.CreatedAtUtc
FROM VisibleCandidates vc
JOIN NearDuplicateGroups nd
    ON nd.FacultyId = vc.FacultyId
   AND ISNULL(nd.PublicationYear, -1) = ISNULL(vc.PublicationYear, -1)
   AND ISNULL(nd.AuthorDisplayName, '') = ISNULL(vc.AuthorDisplayName, '')
   AND nd.NearDuplicateTitleKey = vc.NearDuplicateTitleKey
ORDER BY vc.FacultyDisplayName, vc.PublicationYear DESC, vc.NearDuplicateTitleKey, vc.MatchScore DESC;

PRINT '3) Pending queue near-duplicate summary';
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        pc.FacultyId,
        q.PublicationYear,
        q.AuthorDisplayName,
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
NearDuplicateCounts AS
(
    SELECT
        FacultyId,
        PublicationYear,
        AuthorDisplayName,
        NearDuplicateTitleKey,
        CandidateCount = COUNT(*)
    FROM VisibleCandidates
    GROUP BY
        FacultyId,
        PublicationYear,
        AuthorDisplayName,
        NearDuplicateTitleKey
)
SELECT
    ReviewCardsAfterExactDedup = COUNT(*),
    CardsInNearDuplicateGroups = SUM(CASE WHEN nd.CandidateCount > 1 THEN 1 ELSE 0 END),
    NearDuplicateGroupCount = COUNT(DISTINCT CASE WHEN nd.CandidateCount > 1 THEN CONVERT(NVARCHAR(64), nd.FacultyId) + '|' + CONVERT(NVARCHAR(20), ISNULL(nd.PublicationYear, -1)) + '|' + ISNULL(nd.AuthorDisplayName, '') + '|' + nd.NearDuplicateTitleKey ELSE NULL END)
FROM VisibleCandidates vc
JOIN NearDuplicateCounts nd
    ON nd.FacultyId = vc.FacultyId
   AND ISNULL(nd.PublicationYear, -1) = ISNULL(vc.PublicationYear, -1)
   AND ISNULL(nd.AuthorDisplayName, '') = ISNULL(vc.AuthorDisplayName, '')
   AND nd.NearDuplicateTitleKey = vc.NearDuplicateTitleKey;
