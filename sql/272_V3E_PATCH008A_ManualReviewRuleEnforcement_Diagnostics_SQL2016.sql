/*
V3E PATCH008A - Manual Review Rule Enforcement Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityReviewConstraint', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityReviewConstraint. Run PATCH008 setup first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityCandidateSummary. Run identity summary generation first.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Active manual review rules';
SELECT
    c.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.DepartmentName,
    c.RequiresVerifiedIdentifierForLikelyIdentity,
    c.RequiresManualReviewForInitialOnlyName,
    c.RequiresAuthorSpecificAffiliationEvidence,
    c.RequiresKnownCoauthorOrTopicEvidence,
    c.ConstraintReason,
    c.IsActive
FROM openalex.FacultyIdentityReviewConstraint c
LEFT JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
WHERE c.IsActive = 1
ORDER BY f.DisplayName, c.FacultyId;

PRINT '2) Identity summary rows affected by manual review rules';
SELECT TOP (200)
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.BestCandidateType,
    s.BestIdentityEvidenceScore,
    s.BestIdentityEvidenceStatus,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority,
    s.DiscoveryMethods,
    s.DistinctExpandedWorks,
    s.StrongWorkEvidenceRows,
    s.ModerateWorkEvidenceRows,
    s.AverageWorkEvidenceScore,
    s.BestWorkEvidenceScore,
    s.SummaryText,
    s.EvidenceJson,
    s.UpdatedAtUtc
FROM openalex.FacultyIdentityCandidateSummary s
JOIN openalex.FacultyIdentityReviewConstraint c
    ON s.FacultyId = c.FacultyId
   AND c.IsActive = 1
ORDER BY
    s.FacultyDisplayName,
    s.ReviewPriority,
    s.BestIdentityEvidenceScore DESC,
    s.BestWorkEvidenceScore DESC;

PRINT '3) Constrained summaries by triage status';
SELECT
    s.IdentityReviewTriageStatus,
    COUNT(*) AS SummaryRowCount
FROM openalex.FacultyIdentityCandidateSummary s
JOIN openalex.FacultyIdentityReviewConstraint c
    ON s.FacultyId = c.FacultyId
   AND c.IsActive = 1
GROUP BY s.IdentityReviewTriageStatus
ORDER BY SummaryRowCount DESC, s.IdentityReviewTriageStatus;

PRINT '4) Latest summary-generation runs mentioning manual review rules';
IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
BEGIN
    SELECT TOP (20)
        r.IngestionRunId,
        r.Status,
        r.InstitutionFilter,
        r.ApplicationVersion,
        r.CreatedAtUtc,
        r.FinishedAtUtc,
        r.ElapsedMs,
        summary.SummaryText
    FROM openalex.IngestionRun r
    LEFT JOIN openalex.IngestionRunSummary summary
        ON r.IngestionRunId = summary.IngestionRunId
    WHERE r.InstitutionFilter LIKE N'%FacultyIdentityCandidateSummaryGeneration%'
    ORDER BY r.IngestionRunId DESC;
END;
