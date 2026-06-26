/*
V3E PATCH014 - Accepted Publications Evidence Report Diagnostics
SQL Server 2016-safe.

Purpose:
  Validate accepted publication rows and the evidence fields used by /Reports/AcceptedPublications.
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

PRINT '1) Accepted publications with evidence fields';
SELECT TOP (100)
    d.FacultyPublicationCandidateId,
    f.DisplayName AS FacultyDisplayName,
    f.DepartmentName,
    COALESCE(d.WorkTitle, pc.WorkTitle) AS WorkTitle,
    COALESCE(d.PublicationYear, pc.PublicationYear) AS PublicationYear,
    pc.AuthorDisplayName,
    d.DecisionStatus,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes,
    pc.MatchScore,
    InstitutionEvidencePresent = CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%' THEN 1 ELSE 0 END,
    CoauthorEvidencePresent = CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%' THEN 1 ELSE 0 END,
    EvidenceBoostPresent = CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END,
    NearDuplicateResolutionStatus = res.ResolutionStatus,
    RelatedFacultyPublicationCandidateId = res.RelatedFacultyPublicationCandidateId,
    NearDuplicateResolutionReason = res.ResolutionReason,
    pc.EvidenceSummary
FROM openalex.FacultyPublicationDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted'
ORDER BY f.DisplayName, COALESCE(d.PublicationYear, pc.PublicationYear) DESC, COALESCE(d.WorkTitle, pc.WorkTitle);

PRINT '2) Accepted publication evidence counts';
SELECT
    AcceptedPublicationCount = COUNT(*),
    WithMatchScore = SUM(CASE WHEN pc.MatchScore IS NOT NULL THEN 1 ELSE 0 END),
    WithInstitutionEvidence = SUM(CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%' THEN 1 ELSE 0 END),
    WithCoauthorEvidence = SUM(CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(pc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%' THEN 1 ELSE 0 END),
    WithEvidenceBoost = SUM(CASE WHEN ISNULL(pc.EvidenceSummary, '') LIKE '%EvidenceBoost=%' THEN 1 ELSE 0 END),
    WithNearDuplicateResolution = SUM(CASE WHEN res.ResolutionStatus IS NOT NULL THEN 1 ELSE 0 END),
    WithReviewerNotes = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(d.ReviewerNotes)), '') IS NOT NULL THEN 1 ELSE 0 END)
FROM openalex.FacultyPublicationDecision d
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted';

PRINT '3) Accepted publications by faculty';
SELECT
    f.DisplayName AS FacultyDisplayName,
    f.DepartmentName,
    AcceptedPublicationCount = COUNT(*),
    LatestAcceptedAtUtc = MAX(d.ReviewedAtUtc)
FROM openalex.FacultyPublicationDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted'
GROUP BY f.DisplayName, f.DepartmentName
ORDER BY AcceptedPublicationCount DESC, f.DisplayName;
