/*
V3E PATCH015 - Accepted Publications Evidence CSV Diagnostics
SQL Server 2016-safe.

Purpose:
  Preview the columns now included in the accepted publications evidence CSV export.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationDecision.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationCandidate.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Accepted publications CSV evidence preview';
SELECT TOP (100)
    d.FacultyPublicationCandidateId,
    f.DisplayName AS FacultyDisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    f.DivisionName,
    f.AppointmentTitle,
    pc.AuthorDisplayName,
    pc.AuthorOpenAlexId,
    pc.AuthorOrcid,
    COALESCE(d.WorkTitle, pc.WorkTitle) AS WorkTitle,
    COALESCE(d.PublicationYear, pc.PublicationYear) AS PublicationYear,
    pc.PublicationDate,
    COALESCE(d.WorkDoi, pc.WorkDoi) AS WorkDoi,
    COALESCE(d.WorkOpenAlexId, pc.WorkOpenAlexId) AS WorkOpenAlexId,
    pc.WorkType,
    w.CitedByCount,
    d.DecisionStatus,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes,
    pc.MatchScore,
    EvidenceBoostPresent = CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END,
    AffiliatedInstitutionEvidencePresent = CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%' THEN 1 ELSE 0 END,
    KnownCoauthorEvidencePresent = CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%' THEN 1 ELSE 0 END,
    NearDuplicateResolutionStatus = res.ResolutionStatus,
    RelatedFacultyPublicationCandidateId = res.RelatedFacultyPublicationCandidateId,
    NearDuplicateResolutionReason = res.ResolutionReason,
    pc.EvidenceSummary
FROM openalex.FacultyPublicationDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
LEFT JOIN openalex.Work w
    ON COALESCE(d.WorkId, pc.WorkId) = w.WorkId
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted'
ORDER BY f.DisplayName, COALESCE(d.PublicationYear, pc.PublicationYear) DESC, COALESCE(d.WorkTitle, pc.WorkTitle);

PRINT '2) CSV evidence coverage summary';
SELECT
    AcceptedPublicationCount = COUNT(*),
    WithCandidateId = SUM(CASE WHEN d.FacultyPublicationCandidateId IS NOT NULL THEN 1 ELSE 0 END),
    WithMatchScore = SUM(CASE WHEN pc.MatchScore IS NOT NULL THEN 1 ELSE 0 END),
    WithEvidenceBoost = SUM(CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END),
    WithInstitutionEvidence = SUM(CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%' THEN 1 ELSE 0 END),
    WithCoauthorEvidence = SUM(CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%' THEN 1 ELSE 0 END),
    WithNearDuplicateResolution = SUM(CASE WHEN res.ResolutionStatus IS NOT NULL THEN 1 ELSE 0 END),
    WithReviewerNotes = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(d.ReviewerNotes)), '') IS NOT NULL THEN 1 ELSE 0 END),
    WithEvidenceSummary = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(pc.EvidenceSummary)), '') IS NOT NULL THEN 1 ELSE 0 END)
FROM openalex.FacultyPublicationDecision d
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted';
