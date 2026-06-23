/*
V3E PATCH005 - Identity Review UI Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateSummary does not exist. Run V3E PATCH004 first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateDecision does not exist. Run sql/240_V3E_PATCH005_FacultyIdentityCandidateDecision.sql first.', 16, 1);
    RETURN;
END;

SELECT
    COUNT(*) AS IdentitySummaryRows,
    SUM(CASE WHEN d.FacultyIdentityCandidateDecisionId IS NULL THEN 1 ELSE 0 END) AS UndecidedRows,
    SUM(CASE WHEN d.DecisionStatus = 'AcceptedIdentity' THEN 1 ELSE 0 END) AS AcceptedIdentityRows,
    SUM(CASE WHEN d.DecisionStatus = 'RejectedIdentity' THEN 1 ELSE 0 END) AS RejectedIdentityRows,
    SUM(CASE WHEN d.DecisionStatus = 'NeedsMoreEvidence' THEN 1 ELSE 0 END) AS NeedsMoreEvidenceRows
FROM openalex.FacultyIdentityCandidateSummary s
LEFT JOIN openalex.FacultyIdentityCandidateDecision d
    ON s.FacultyId = d.FacultyId
   AND s.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1;

SELECT
    s.IdentityReviewTriageStatus,
    COUNT(*) AS SummaryRows,
    SUM(CASE WHEN d.FacultyIdentityCandidateDecisionId IS NULL THEN 1 ELSE 0 END) AS UndecidedRows,
    SUM(CASE WHEN d.DecisionStatus = 'AcceptedIdentity' THEN 1 ELSE 0 END) AS AcceptedIdentityRows,
    SUM(CASE WHEN d.DecisionStatus = 'RejectedIdentity' THEN 1 ELSE 0 END) AS RejectedIdentityRows,
    SUM(CASE WHEN d.DecisionStatus = 'NeedsMoreEvidence' THEN 1 ELSE 0 END) AS NeedsMoreEvidenceRows
FROM openalex.FacultyIdentityCandidateSummary s
LEFT JOIN openalex.FacultyIdentityCandidateDecision d
    ON s.FacultyId = d.FacultyId
   AND s.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1
GROUP BY s.IdentityReviewTriageStatus
ORDER BY
    MIN(s.ReviewPriority),
    s.IdentityReviewTriageStatus;

SELECT TOP (100)
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority,
    s.BestCandidateType,
    s.BestIdentityEvidenceScore,
    s.BestWorkEvidenceScore,
    d.DecisionStatus AS CurrentDecisionStatus,
    d.ReviewerName,
    d.ReviewNotes,
    d.CreatedAtUtc AS DecisionCreatedAtUtc
FROM openalex.FacultyIdentityCandidateSummary s
LEFT JOIN openalex.FacultyIdentityCandidateDecision d
    ON s.FacultyId = d.FacultyId
   AND s.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1
ORDER BY
    CASE WHEN d.DecisionStatus IS NULL THEN 0 ELSE 1 END,
    s.FacultyDisplayName,
    s.ReviewPriority,
    s.BestIdentityEvidenceScore DESC,
    s.BestWorkEvidenceScore DESC;
