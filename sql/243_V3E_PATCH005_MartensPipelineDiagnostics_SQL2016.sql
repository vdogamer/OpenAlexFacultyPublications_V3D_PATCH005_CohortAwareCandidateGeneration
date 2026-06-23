/*
V3E PATCH005 - Martens Pipeline Diagnostics
SQL Server 2016-safe.

Purpose:
  Diagnose why Jeff/Jeffrey Martens does or does not appear in /Review/Identities.

This checks each stage:
  1. openalex.Faculty
  2. openalex.PilotFacultyCohort
  3. openalex.FacultyIdentityCandidate
  4. openalex.FacultyIdentityCandidateWork
  5. openalex.FacultyIdentityWorkEvidenceScore
  6. openalex.FacultyIdentityCandidateSummary
  7. latest ingestion runs
*/

SET NOCOUNT ON;

DECLARE @FacultyNameFilter NVARCHAR(200) = N'%Martens%';
DECLARE @ExpectedCohortName NVARCHAR(200) = N'Pilot Expansion 16 Faculty';

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

PRINT '1) Faculty rows matching Martens';
SELECT
    FacultyId,
    DisplayName,
    FirstName,
    LastName,
    Orcid,
    ClaimedOpenAlexAuthorId,
    IsActive,
    DepartmentName,
    AppointmentTitle
FROM openalex.Faculty
WHERE DisplayName LIKE @FacultyNameFilter
   OR FirstName LIKE @FacultyNameFilter
   OR LastName LIKE @FacultyNameFilter
ORDER BY DisplayName;

PRINT '2) Pilot cohort rows matching Martens';
IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.PilotFacultyCohort' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        c.CohortName,
        c.PilotOrder,
        c.IsEnabled AS CohortIsEnabled,
        f.FacultyId,
        f.DisplayName,
        f.IsActive
    FROM openalex.PilotFacultyCohort c
    JOIN openalex.Faculty f
        ON c.FacultyId = f.FacultyId
    WHERE f.DisplayName LIKE @FacultyNameFilter
       OR f.FirstName LIKE @FacultyNameFilter
       OR f.LastName LIKE @FacultyNameFilter
    ORDER BY c.CohortName, c.PilotOrder;

    SELECT
        CohortName,
        COUNT(*) AS CohortRows,
        SUM(CASE WHEN IsEnabled = 1 THEN 1 ELSE 0 END) AS EnabledRows,
        MIN(PilotOrder) AS MinPilotOrder,
        MAX(PilotOrder) AS MaxPilotOrder
    FROM openalex.PilotFacultyCohort
    WHERE CohortName IN (N'Pilot Expansion 15 Faculty', @ExpectedCohortName)
    GROUP BY CohortName
    ORDER BY CohortName;
END;

PRINT '3) Identity candidate rows matching Martens faculty';
IF OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityCandidate. Run FacultyIdentityCandidateGeneration.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        f.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        c.CandidateAuthorOpenAlexShortId,
        c.CandidateDisplayName,
        c.CandidateOrcid,
        c.DiscoveryMethod,
        c.EvidenceScore,
        c.EvidenceStatus,
        c.EvidenceSummary,
        c.CreatedAtUtc
    FROM openalex.FacultyIdentityCandidate c
    JOIN openalex.Faculty f
        ON c.FacultyId = f.FacultyId
    WHERE f.DisplayName LIKE @FacultyNameFilter
       OR f.FirstName LIKE @FacultyNameFilter
       OR f.LastName LIKE @FacultyNameFilter
    ORDER BY c.EvidenceScore DESC, c.CandidateDisplayName;
END;

PRINT '4) Candidate work rows matching Martens faculty';
IF OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityCandidateWork. Run FacultyIdentityCandidateWorkExpansion.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        f.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        COUNT(*) AS CandidateWorkRows,
        COUNT(DISTINCT w.WorkOpenAlexId) AS DistinctWorks
    FROM openalex.FacultyIdentityCandidateWork w
    JOIN openalex.Faculty f
        ON w.FacultyId = f.FacultyId
    WHERE f.DisplayName LIKE @FacultyNameFilter
       OR f.FirstName LIKE @FacultyNameFilter
       OR f.LastName LIKE @FacultyNameFilter
    GROUP BY f.FacultyId, f.DisplayName
    ORDER BY f.DisplayName;
END;

PRINT '5) Work evidence score rows matching Martens faculty';
IF OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityWorkEvidenceScore. Run FacultyIdentityWorkEvidenceScoring.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        f.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        s.CandidateAuthorOpenAlexShortId,
        s.MatchedAuthorDisplayName,
        COUNT(*) AS ScoredWorkRows,
        SUM(CASE WHEN s.WorkEvidenceStatus = 'StrongWorkEvidence' THEN 1 ELSE 0 END) AS StrongWorkEvidenceRows,
        SUM(CASE WHEN s.WorkEvidenceStatus = 'ModerateWorkEvidence' THEN 1 ELSE 0 END) AS ModerateWorkEvidenceRows,
        SUM(CASE WHEN s.WorkEvidenceStatus = 'ConflictingWorkEvidence' THEN 1 ELSE 0 END) AS ConflictingWorkEvidenceRows,
        AVG(CAST(s.OverallWorkEvidenceScore AS DECIMAL(9,4))) AS AverageWorkEvidenceScore,
        MAX(s.OverallWorkEvidenceScore) AS BestWorkEvidenceScore
    FROM openalex.FacultyIdentityWorkEvidenceScore s
    JOIN openalex.Faculty f
        ON s.FacultyId = f.FacultyId
    WHERE f.DisplayName LIKE @FacultyNameFilter
       OR f.FirstName LIKE @FacultyNameFilter
       OR f.LastName LIKE @FacultyNameFilter
    GROUP BY
        f.FacultyId,
        f.DisplayName,
        s.CandidateAuthorOpenAlexShortId,
        s.MatchedAuthorDisplayName
    ORDER BY f.DisplayName, BestWorkEvidenceScore DESC;
END;

PRINT '6) Identity summary rows matching Martens faculty';
IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityCandidateSummary. Run sql/230 and FacultyIdentityCandidateSummaryGeneration.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        FacultyId,
        FacultyDisplayName,
        CandidateAuthorOpenAlexShortId,
        CandidateDisplayName,
        CandidateOrcid,
        IdentityReviewTriageStatus,
        ReviewPriority,
        BestCandidateType,
        BestIdentityEvidenceScore,
        BestWorkEvidenceScore,
        DistinctExpandedWorks,
        ScoredWorkRows,
        StrongWorkEvidenceRows,
        ModerateWorkEvidenceRows,
        ConflictingWorkEvidenceRows,
        SummaryText,
        CreatedAtUtc,
        UpdatedAtUtc
    FROM openalex.FacultyIdentityCandidateSummary
    WHERE FacultyDisplayName LIKE @FacultyNameFilter
       OR CandidateDisplayName LIKE @FacultyNameFilter
    ORDER BY ReviewPriority, BestIdentityEvidenceScore DESC, BestWorkEvidenceScore DESC;
END;

PRINT '7) Latest V3E identity-related ingestion runs';
IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.IngestionRun' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT TOP (20)
        IngestionRunId,
        Status,
        InstitutionFilter,
        MaxRecordsPerEntity,
        ApplicationVersion,
        CreatedAtUtc,
        FinishedAtUtc,
        ElapsedMs
    FROM openalex.IngestionRun
    WHERE InstitutionFilter LIKE N'%FacultyIdentity%'
       OR ApplicationVersion LIKE N'%Identity%'
       OR ApplicationVersion LIKE N'%PATCH004%'
       OR ApplicationVersion LIKE N'%PATCH005%'
    ORDER BY IngestionRunId DESC;
END;
