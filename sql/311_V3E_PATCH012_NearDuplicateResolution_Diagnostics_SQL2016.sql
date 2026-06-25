/*
V3E PATCH012 - Near-Duplicate Publication Resolution Diagnostics
SQL Server 2016-safe.

Purpose:
  Show current reviewer resolutions for possible near-duplicate publication candidates.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationNearDuplicateResolution. Run sql/310_V3E_PATCH012_NearDuplicateResolution_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Current near-duplicate resolutions';
SELECT
    r.FacultyPublicationNearDuplicateResolutionId,
    r.FacultyPublicationCandidateId,
    pc.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    pc.WorkId,
    pc.WorkOpenAlexId,
    pc.WorkTitle,
    pc.PublicationYear,
    r.RelatedFacultyPublicationCandidateId,
    related.WorkId AS RelatedWorkId,
    related.WorkOpenAlexId AS RelatedWorkOpenAlexId,
    related.WorkTitle AS RelatedWorkTitle,
    r.ResolutionStatus,
    r.ResolutionReason,
    r.ReviewerName,
    r.IsCurrent,
    r.CreatedAtUtc,
    r.UpdatedAtUtc
FROM openalex.FacultyPublicationNearDuplicateResolution r
JOIN openalex.FacultyPublicationCandidate pc
    ON r.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
LEFT JOIN openalex.FacultyPublicationCandidate related
    ON r.RelatedFacultyPublicationCandidateId = related.FacultyPublicationCandidateId
LEFT JOIN openalex.Faculty f
    ON pc.FacultyId = f.FacultyId
WHERE r.IsCurrent = 1
ORDER BY r.UpdatedAtUtc DESC, r.FacultyPublicationCandidateId;

PRINT '2) Resolution counts';
SELECT
    ResolutionStatus,
    CurrentResolutionCount = COUNT(*)
FROM openalex.FacultyPublicationNearDuplicateResolution
WHERE IsCurrent = 1
GROUP BY ResolutionStatus
ORDER BY CurrentResolutionCount DESC, ResolutionStatus;

PRINT '3) Historical resolution rows';
SELECT TOP (100)
    r.FacultyPublicationNearDuplicateResolutionId,
    r.FacultyPublicationCandidateId,
    r.RelatedFacultyPublicationCandidateId,
    r.ResolutionStatus,
    r.ResolutionReason,
    r.ReviewerName,
    r.IsCurrent,
    r.CreatedAtUtc,
    r.UpdatedAtUtc
FROM openalex.FacultyPublicationNearDuplicateResolution r
ORDER BY r.UpdatedAtUtc DESC, r.FacultyPublicationNearDuplicateResolutionId DESC;
