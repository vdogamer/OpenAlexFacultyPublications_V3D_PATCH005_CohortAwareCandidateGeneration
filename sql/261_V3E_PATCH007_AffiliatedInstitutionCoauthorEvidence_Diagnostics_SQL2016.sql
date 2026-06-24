/*
V3E PATCH007 - Affiliated Institution and Known Coauthor Evidence Diagnostics
SQL Server 2016-safe.

Purpose:
  Validate that PATCH007 affiliated-institution and known-coauthor evidence is being appended
  to publication candidate rows.
*/

SET NOCOUNT ON;

DECLARE @FacultyNameFilter NVARCHAR(200) = N'%Martens%';

PRINT '1) Affiliated institution aliases';
IF OBJECT_ID(N'openalex.AffiliatedInstitutionAlias', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AffiliatedInstitutionAlias. Run sql/260_V3E_PATCH007_AffiliatedInstitutionAlias_SQL2016.sql first.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT
        InstitutionGroupName,
        InstitutionDisplayName,
        NormalizedInstitutionName,
        EvidenceWeight,
        IsActive
    FROM openalex.AffiliatedInstitutionAlias
    ORDER BY InstitutionGroupName, EvidenceWeight DESC, InstitutionDisplayName;
END;

PRINT '2) Publication candidates with PATCH007 evidence summary';
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
        pc.PublicationYear,
        pc.AuthorOpenAlexId,
        pc.AuthorDisplayName,
        pc.CandidateSource,
        pc.CandidateStatus,
        pc.MatchScore,
        pc.EvidenceSummary,
        pc.EvidenceJson,
        pc.IngestionRunId,
        pc.UpdatedAtUtc
    FROM openalex.FacultyPublicationCandidate pc
    JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    WHERE pc.EvidenceSummary LIKE N'%PATCH007Evidence%'
      AND (f.DisplayName LIKE @FacultyNameFilter OR pc.AuthorDisplayName LIKE @FacultyNameFilter OR pc.WorkTitle LIKE @FacultyNameFilter)
    ORDER BY pc.MatchScore DESC, pc.PublicationYear DESC, pc.WorkTitle;
END;

PRINT '3) Candidate-level affiliated institution matches recomputed from current tables';
IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AffiliatedInstitutionAlias', N'U') IS NULL
BEGIN
    SELECT 'Missing required tables for affiliated institution recompute.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT TOP (100)
        pc.FacultyPublicationCandidateId,
        f.DisplayName AS FacultyDisplayName,
        pc.WorkTitle,
        pc.AuthorDisplayName,
        COUNT(DISTINCT i.OpenAlexId) AS AffiliatedInstitutionMatchCount,
        MAX(aia.EvidenceWeight) AS BestAffiliatedInstitutionEvidenceWeight,
        STUFF
        (
            (
                SELECT DISTINCT ', ' + i2.DisplayName
                FROM openalex.WorkAuthorship wa2
                JOIN openalex.WorkAuthorshipInstitution wai2
                    ON wa2.WorkAuthorshipId = wai2.WorkAuthorshipId
                JOIN openalex.Institution i2
                    ON wai2.InstitutionOpenAlexId = i2.OpenAlexId
                JOIN openalex.AffiliatedInstitutionAlias aia2
                    ON aia2.IsActive = 1
                   AND
                   (
                       LOWER(LTRIM(RTRIM(i2.DisplayName))) = aia2.NormalizedInstitutionName
                       OR LOWER(LTRIM(RTRIM(i2.DisplayName))) LIKE '%' + aia2.NormalizedInstitutionName + '%'
                   )
                WHERE wa2.WorkId = pc.WorkId
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'),
            1,
            2,
            ''
        ) AS AffiliatedInstitutionNames
    FROM openalex.FacultyPublicationCandidate pc
    JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    JOIN openalex.WorkAuthorship wa
        ON pc.WorkId = wa.WorkId
    JOIN openalex.WorkAuthorshipInstitution wai
        ON wa.WorkAuthorshipId = wai.WorkAuthorshipId
    JOIN openalex.Institution i
        ON wai.InstitutionOpenAlexId = i.OpenAlexId
    JOIN openalex.AffiliatedInstitutionAlias aia
        ON aia.IsActive = 1
       AND
       (
           LOWER(LTRIM(RTRIM(i.DisplayName))) = aia.NormalizedInstitutionName
           OR LOWER(LTRIM(RTRIM(i.DisplayName))) LIKE '%' + aia.NormalizedInstitutionName + '%'
       )
    WHERE f.DisplayName LIKE @FacultyNameFilter
       OR pc.AuthorDisplayName LIKE @FacultyNameFilter
       OR pc.WorkTitle LIKE @FacultyNameFilter
    GROUP BY
        pc.FacultyPublicationCandidateId,
        f.DisplayName,
        pc.WorkId,
        pc.WorkTitle,
        pc.AuthorDisplayName
    ORDER BY BestAffiliatedInstitutionEvidenceWeight DESC, AffiliatedInstitutionMatchCount DESC, pc.WorkTitle;
END;

PRINT '4) Candidate-level known coauthor overlap recomputed from accepted publications';
IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
BEGIN
    SELECT 'Missing required tables for known coauthor recompute.' AS DiagnosticMessage;
END;
ELSE
BEGIN
    SELECT TOP (100)
        pc.FacultyPublicationCandidateId,
        f.DisplayName AS FacultyDisplayName,
        pc.WorkTitle,
        pc.AuthorDisplayName,
        COUNT(DISTINCT newWa.AuthorOpenAlexId) AS KnownCoauthorOverlapCount,
        COUNT(DISTINCT priorDecision.WorkId) AS KnownCoauthorHistoricalPublicationCount,
        STUFF
        (
            (
                SELECT DISTINCT ', ' + ISNULL(newWa2.AuthorDisplayName, newWa2.AuthorOpenAlexId)
                FROM openalex.WorkAuthorship newWa2
                JOIN openalex.WorkAuthorship priorWa2
                    ON priorWa2.AuthorOpenAlexId = newWa2.AuthorOpenAlexId
                JOIN openalex.FacultyPublicationDecision priorDecision2
                    ON priorDecision2.WorkId = priorWa2.WorkId
                   AND priorDecision2.FacultyId = pc.FacultyId
                   AND priorDecision2.IsCurrent = 1
                   AND priorDecision2.DecisionStatus = 'Accepted'
                   AND priorDecision2.WorkId <> pc.WorkId
                WHERE newWa2.WorkId = pc.WorkId
                  AND newWa2.AuthorOpenAlexId IS NOT NULL
                  AND newWa2.AuthorOpenAlexId <> pc.AuthorOpenAlexId
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'),
            1,
            2,
            ''
        ) AS KnownCoauthorNames
    FROM openalex.FacultyPublicationCandidate pc
    JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    JOIN openalex.WorkAuthorship newWa
        ON newWa.WorkId = pc.WorkId
    JOIN openalex.WorkAuthorship priorWa
        ON priorWa.AuthorOpenAlexId = newWa.AuthorOpenAlexId
    JOIN openalex.FacultyPublicationDecision priorDecision
        ON priorDecision.WorkId = priorWa.WorkId
       AND priorDecision.FacultyId = pc.FacultyId
       AND priorDecision.IsCurrent = 1
       AND priorDecision.DecisionStatus = 'Accepted'
       AND priorDecision.WorkId <> pc.WorkId
    WHERE newWa.AuthorOpenAlexId IS NOT NULL
      AND newWa.AuthorOpenAlexId <> pc.AuthorOpenAlexId
      AND (f.DisplayName LIKE @FacultyNameFilter OR pc.AuthorDisplayName LIKE @FacultyNameFilter OR pc.WorkTitle LIKE @FacultyNameFilter)
    GROUP BY
        pc.FacultyPublicationCandidateId,
        f.DisplayName,
        pc.FacultyId,
        pc.WorkId,
        pc.WorkTitle,
        pc.AuthorOpenAlexId,
        pc.AuthorDisplayName
    ORDER BY KnownCoauthorOverlapCount DESC, KnownCoauthorHistoricalPublicationCount DESC, pc.WorkTitle;
END;

PRINT '5) Latest PATCH007 worker runs';
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
    WHERE ApplicationVersion LIKE N'%PATCH007%'
       OR InstitutionFilter LIKE N'%FacultyPublicationCandidateGeneration%'
    ORDER BY IngestionRunId DESC;
END;
