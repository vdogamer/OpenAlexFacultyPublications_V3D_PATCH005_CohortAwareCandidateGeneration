/*
V3E PATCH010 - Publication Candidate Deduplication and Review Grouping Diagnostics
SQL Server 2016-safe.

Purpose:
  Show duplicate pending publication candidate groups by FacultyId + WorkId.
  PATCH010 does not delete duplicate candidate rows. It shows one best review card per faculty/work pair.
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

PRINT '1) Duplicate pending candidate groups by faculty/work';
;WITH PendingCandidates AS
(
    SELECT
        pc.FacultyId,
        q.FacultyDisplayName,
        pc.WorkId,
        q.WorkOpenAlexId,
        q.WorkTitle,
        q.PublicationYear,
        q.AuthorDisplayName,
        q.FacultyPublicationCandidateId,
        q.MatchScore,
        q.CandidateStatus,
        q.CreatedAtUtc,
        CandidateGroupKey = COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle)
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
Grouped AS
(
    SELECT
        FacultyId,
        FacultyDisplayName,
        WorkId,
        WorkOpenAlexId,
        WorkTitle,
        PublicationYear,
        CandidateGroupKey,
        DuplicateCandidateCount = COUNT(*),
        BestMatchScore = MAX(MatchScore),
        OldestCandidateCreatedAtUtc = MIN(CreatedAtUtc),
        NewestCandidateCreatedAtUtc = MAX(CreatedAtUtc)
    FROM PendingCandidates
    GROUP BY
        FacultyId,
        FacultyDisplayName,
        WorkId,
        WorkOpenAlexId,
        WorkTitle,
        PublicationYear,
        CandidateGroupKey
)
SELECT
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    PublicationYear,
    DuplicateCandidateCount,
    BestMatchScore,
    OldestCandidateCreatedAtUtc,
    NewestCandidateCreatedAtUtc
FROM Grouped
WHERE DuplicateCandidateCount > 1
ORDER BY DuplicateCandidateCount DESC, BestMatchScore DESC, FacultyDisplayName, WorkTitle;

PRINT '2) Best pending review row per faculty/work group';
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
        q.EvidenceSummary,
        q.CreatedAtUtc,
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
)
SELECT TOP (100)
    FacultyPublicationCandidateId,
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    PublicationYear,
    AuthorDisplayName,
    CandidateStatus,
    MatchScore,
    DuplicateCandidateCount,
    HiddenDuplicateCandidateCount = DuplicateCandidateCount - 1,
    CreatedAtUtc
FROM PendingCandidates
WHERE ReviewGroupRank = 1
ORDER BY CreatedAtUtc DESC;

PRINT '3) Pending queue before and after review grouping';
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        pc.FacultyId,
        CandidateGroupKey = COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle),
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
)
SELECT
    PendingCandidateRowsBeforeGrouping = COUNT(*),
    ReviewCardsAfterGrouping = SUM(CASE WHEN ReviewGroupRank = 1 THEN 1 ELSE 0 END),
    HiddenDuplicateRows = COUNT(*) - SUM(CASE WHEN ReviewGroupRank = 1 THEN 1 ELSE 0 END)
FROM PendingCandidates;
