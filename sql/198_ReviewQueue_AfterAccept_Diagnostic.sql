/*
V3D PATCH004 — Review Queue After Accept Diagnostic

Run after clicking Accept or Reject on /Review/Authors.
*/

SET NOCOUNT ON;

PRINT 'Author candidate status counts';

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL
BEGIN
    SELECT
        CandidateStatus,
        COUNT_BIG(*) AS CandidateCount
    FROM openalex.FacultyAuthorMatchCandidate
    GROUP BY CandidateStatus
    ORDER BY CandidateStatus;
END

PRINT 'Author decision counts';

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NOT NULL
BEGIN
    SELECT
        DecisionStatus,
        IsCurrent,
        COUNT_BIG(*) AS DecisionCount
    FROM openalex.FacultyAuthorMatchDecision
    GROUP BY DecisionStatus, IsCurrent
    ORDER BY DecisionStatus, IsCurrent DESC;
END

PRINT 'Candidates that should still appear in /Review/Authors after PATCH004';

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL
BEGIN
    SELECT
        c.FacultyAuthorMatchCandidateId,
        c.FacultyId,
        c.AuthorOpenAlexId,
        c.AuthorDisplayName,
        c.CandidateStatus,
        c.MatchScore,
        c.CreatedAtUtc
    FROM openalex.FacultyAuthorMatchCandidate c
    WHERE
        (
            c.CandidateStatus IS NULL
            OR c.CandidateStatus IN ('PendingReview', 'NeedsMoreEvidence')
        )
        AND NOT EXISTS
        (
            SELECT 1
            FROM openalex.FacultyAuthorMatchDecision d
            WHERE d.FacultyAuthorMatchCandidateId = c.FacultyAuthorMatchCandidateId
              AND d.IsCurrent = 1
              AND d.DecisionStatus IN ('Accepted', 'Rejected')
        )
    ORDER BY c.CreatedAtUtc DESC;
END

PRINT 'Recent author decisions';

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50
        FacultyAuthorMatchDecisionId,
        FacultyAuthorMatchCandidateId,
        FacultyId,
        AuthorOpenAlexId,
        AuthorDisplayName,
        DecisionStatus,
        IsCurrent,
        ReviewedBy,
        ReviewedAtUtc,
        ReviewerNotes
    FROM openalex.FacultyAuthorMatchDecision
    ORDER BY FacultyAuthorMatchDecisionId DESC;
END
