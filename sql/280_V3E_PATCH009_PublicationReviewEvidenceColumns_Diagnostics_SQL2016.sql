/*
V3E PATCH009 - Publication Review Evidence Columns Diagnostics
SQL Server 2016-safe.

Purpose:
  Verify that publication candidates contain PATCH007 evidence values that the PATCH009 UI can display as separate columns.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.vw_FacultyPublicationReviewQueue', N'V') IS NULL
BEGIN
    SELECT 'Missing openalex.vw_FacultyPublicationReviewQueue.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Pending publication review rows with PATCH007 evidence in EvidenceSummary';
SELECT TOP (100)
    q.FacultyPublicationCandidateId,
    q.FacultyDisplayName,
    q.DepartmentName,
    q.WorkTitle,
    q.PublicationYear,
    q.AuthorDisplayName,
    q.CandidateStatus,
    q.MatchScore,
    InstitutionEvidencePresent = CASE WHEN q.EvidenceSummary LIKE '%AffiliatedInstitutionMatchCount=%' THEN 1 ELSE 0 END,
    CoauthorEvidencePresent = CASE WHEN q.EvidenceSummary LIKE '%KnownCoauthorOverlapCount=%' THEN 1 ELSE 0 END,
    EvidenceBoostPresent = CASE WHEN q.EvidenceSummary LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END,
    q.EvidenceSummary,
    q.CreatedAtUtc
FROM openalex.vw_FacultyPublicationReviewQueue q
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
ORDER BY q.CreatedAtUtc DESC;

PRINT '2) Pending rows grouped by PATCH007 evidence presence';
SELECT
    InstitutionEvidencePresent = CASE WHEN q.EvidenceSummary LIKE '%AffiliatedInstitutionMatchCount=%' THEN 1 ELSE 0 END,
    CoauthorEvidencePresent = CASE WHEN q.EvidenceSummary LIKE '%KnownCoauthorOverlapCount=%' THEN 1 ELSE 0 END,
    EvidenceBoostPresent = CASE WHEN q.EvidenceSummary LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END,
    RowCount = COUNT(*)
FROM openalex.vw_FacultyPublicationReviewQueue q
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
GROUP BY
    CASE WHEN q.EvidenceSummary LIKE '%AffiliatedInstitutionMatchCount=%' THEN 1 ELSE 0 END,
    CASE WHEN q.EvidenceSummary LIKE '%KnownCoauthorOverlapCount=%' THEN 1 ELSE 0 END,
    CASE WHEN q.EvidenceSummary LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END
ORDER BY RowCount DESC;

PRINT '3) Latest publication candidate generation runs';
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
        s.SummaryText
    FROM openalex.IngestionRun r
    LEFT JOIN openalex.IngestionRunSummary s
        ON r.IngestionRunId = s.IngestionRunId
    WHERE r.InstitutionFilter LIKE N'%FacultyPublicationCandidateGeneration%'
    ORDER BY r.IngestionRunId DESC;
END;
