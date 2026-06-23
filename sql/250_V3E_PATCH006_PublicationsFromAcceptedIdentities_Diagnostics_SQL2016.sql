/*
V3E PATCH006 - Publications From Accepted Identity Decisions Diagnostics
SQL Server 2016-safe.

Purpose:
  Verify that AcceptedIdentity decisions are bridged into the existing author-decision workflow
  and then produce publication candidates.
*/

SET NOCOUNT ON;

DECLARE @FacultyNameFilter NVARCHAR(200) = N'%Martens%';

PRINT '1) Current accepted identity decisions';
IF OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    SELECT 'Missing identity decision or summary table.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        s.FacultyId,
        s.FacultyDisplayName,
        s.CandidateAuthorOpenAlexShortId,
        s.CandidateDisplayName,
        s.CandidateOrcid,
        d.DecisionStatus,
        d.ReviewerName,
        d.CreatedAtUtc AS DecisionCreatedAtUtc
    FROM openalex.FacultyIdentityCandidateDecision d
    JOIN openalex.FacultyIdentityCandidateSummary s
        ON d.FacultyId = s.FacultyId
       AND d.CandidateAuthorOpenAlexId = s.CandidateAuthorOpenAlexId
    WHERE d.IsCurrent = 1
      AND d.DecisionStatus = 'AcceptedIdentity'
      AND (s.FacultyDisplayName LIKE @FacultyNameFilter OR s.CandidateDisplayName LIKE @FacultyNameFilter)
    ORDER BY s.FacultyDisplayName, s.CandidateDisplayName;
END;

PRINT '2) Bridged author candidates from accepted identities';
IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyAuthorMatchCandidate.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        c.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        c.FacultyAuthorMatchCandidateId,
        c.AuthorId,
        c.AuthorOpenAlexShortId,
        c.AuthorDisplayName,
        c.AuthorOrcid,
        c.CandidateSource,
        c.CandidateStatus,
        c.MatchScore,
        c.EvidenceSummary,
        c.CreatedAtUtc,
        c.UpdatedAtUtc
    FROM openalex.FacultyAuthorMatchCandidate c
    JOIN openalex.Faculty f
        ON c.FacultyId = f.FacultyId
    WHERE c.CandidateSource = 'AcceptedIdentityDecision'
      AND (f.DisplayName LIKE @FacultyNameFilter OR c.AuthorDisplayName LIKE @FacultyNameFilter)
    ORDER BY f.DisplayName, c.MatchScore DESC, c.AuthorDisplayName;
END;

PRINT '3) Bridged current accepted author decisions';
IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyAuthorMatchDecision.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        d.FacultyAuthorMatchDecisionId,
        d.FacultyAuthorMatchCandidateId,
        d.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        d.AuthorId,
        d.AuthorOpenAlexId,
        d.AuthorDisplayName,
        d.AuthorOrcid,
        d.DecisionStatus,
        d.DecisionReason,
        d.DecisionSource,
        d.IsCurrent,
        d.ReviewedBy,
        d.ReviewedAtUtc
    FROM openalex.FacultyAuthorMatchDecision d
    JOIN openalex.Faculty f
        ON d.FacultyId = f.FacultyId
    WHERE d.IsCurrent = 1
      AND d.DecisionStatus = 'Accepted'
      AND d.DecisionSource = 'IdentityReviewBridge'
      AND (f.DisplayName LIKE @FacultyNameFilter OR d.AuthorDisplayName LIKE @FacultyNameFilter)
    ORDER BY f.DisplayName, d.AuthorDisplayName;
END;

PRINT '4) Publication candidates generated from bridged identity decisions';
IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyPublicationCandidate.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT TOP (100)
        pc.FacultyPublicationCandidateId,
        pc.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        pc.WorkId,
        pc.WorkOpenAlexShortId,
        pc.WorkTitle,
        pc.WorkDoi,
        pc.PublicationYear,
        pc.AuthorOpenAlexId,
        pc.AuthorDisplayName,
        pc.CandidateSource,
        pc.CandidateStatus,
        pc.MatchScore,
        d.DecisionSource AS AuthorDecisionSource,
        pc.CreatedAtUtc,
        pc.UpdatedAtUtc
    FROM openalex.FacultyPublicationCandidate pc
    JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    LEFT JOIN openalex.FacultyAuthorMatchDecision d
        ON pc.FacultyAuthorMatchDecisionId = d.FacultyAuthorMatchDecisionId
    WHERE d.DecisionSource = 'IdentityReviewBridge'
      AND (f.DisplayName LIKE @FacultyNameFilter OR pc.AuthorDisplayName LIKE @FacultyNameFilter OR pc.WorkTitle LIKE @FacultyNameFilter)
    ORDER BY f.DisplayName, pc.PublicationYear DESC, pc.WorkTitle;
END;

PRINT '5) Latest PATCH006 publication candidate generation runs';
IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.IngestionRun.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT TOP (10)
        IngestionRunId,
        Status,
        InstitutionFilter,
        MaxRecordsPerEntity,
        ApplicationVersion,
        CreatedAtUtc,
        FinishedAtUtc,
        ElapsedMs
    FROM openalex.IngestionRun
    WHERE InstitutionFilter LIKE N'%FacultyPublicationCandidateGeneration%'
       OR ApplicationVersion LIKE N'%PATCH006%'
    ORDER BY IngestionRunId DESC;
END;
