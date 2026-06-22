/*
V3E PATCH002 - Identity Candidate Work Expansion Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

DECLARE @PilotCohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';
DECLARE @FacultyNameFilter NVARCHAR(200) = N'%Pollart%';

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidate does not exist. Run V3E PATCH001/001A first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateWork does not exist. Run sql/210_V3E_PATCH002_FacultyIdentityCandidateWork.sql first.', 16, 1);
    RETURN;
END;

PRINT 'V3E PATCH002 Identity Candidate Work Expansion Diagnostics';
SELECT @PilotCohortName AS SelectedCohortName;

;WITH SelectedCohortFaculty AS
(
    SELECT
        pc.FacultyId,
        pc.PilotOrder,
        f.DisplayName
    FROM openalex.PilotFacultyCohort pc
    INNER JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    WHERE pc.CohortName = @PilotCohortName
      AND pc.IsEnabled = 1
      AND f.IsActive = 1
),
CandidateCounts AS
(
    SELECT
        FacultyId,
        COUNT(DISTINCT CandidateAuthorOpenAlexId) AS DistinctIdentityCandidates
    FROM openalex.FacultyIdentityCandidate
    GROUP BY FacultyId
),
WorkCounts AS
(
    SELECT
        FacultyId,
        COUNT(*) AS ExpandedWorkRows,
        COUNT(DISTINCT WorkOpenAlexId) AS DistinctExpandedWorks,
        COUNT(DISTINCT CandidateAuthorOpenAlexId) AS CandidateAuthorsWithWorks,
        MIN(PublicationYear) AS EarliestPublicationYear,
        MAX(PublicationYear) AS LatestPublicationYear
    FROM openalex.FacultyIdentityCandidateWork
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    ISNULL(cc.DistinctIdentityCandidates, 0) AS DistinctIdentityCandidates,
    ISNULL(wc.CandidateAuthorsWithWorks, 0) AS CandidateAuthorsWithWorks,
    ISNULL(wc.ExpandedWorkRows, 0) AS ExpandedWorkRows,
    ISNULL(wc.DistinctExpandedWorks, 0) AS DistinctExpandedWorks,
    wc.EarliestPublicationYear,
    wc.LatestPublicationYear
FROM SelectedCohortFaculty scf
LEFT JOIN CandidateCounts cc
    ON scf.FacultyId = cc.FacultyId
LEFT JOIN WorkCounts wc
    ON scf.FacultyId = wc.FacultyId
ORDER BY scf.PilotOrder, scf.DisplayName, scf.FacultyId;

;WITH SelectedCohortFaculty AS
(
    SELECT
        pc.FacultyId,
        pc.PilotOrder,
        f.DisplayName
    FROM openalex.PilotFacultyCohort pc
    INNER JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    WHERE pc.CohortName = @PilotCohortName
      AND pc.IsEnabled = 1
      AND f.IsActive = 1
)
SELECT
    scf.FacultyId,
    scf.DisplayName AS FacultyName,
    w.CandidateAuthorOpenAlexShortId,
    MAX(w.MatchedAuthorDisplayName) AS MatchedAuthorDisplayName,
    COUNT(*) AS WorkRows,
    COUNT(DISTINCT w.WorkOpenAlexId) AS DistinctWorks,
    MIN(w.PublicationYear) AS EarliestPublicationYear,
    MAX(w.PublicationYear) AS LatestPublicationYear
FROM SelectedCohortFaculty scf
INNER JOIN openalex.FacultyIdentityCandidateWork w
    ON scf.FacultyId = w.FacultyId
GROUP BY
    scf.FacultyId,
    scf.DisplayName,
    scf.PilotOrder,
    w.CandidateAuthorOpenAlexShortId
ORDER BY
    scf.PilotOrder,
    scf.DisplayName,
    COUNT(DISTINCT w.WorkOpenAlexId) DESC,
    w.CandidateAuthorOpenAlexShortId;

SELECT TOP (100)
    f.FacultyId,
    f.DisplayName AS FacultyName,
    w.CandidateAuthorOpenAlexShortId,
    w.MatchedAuthorDisplayName,
    w.MatchedAuthorPosition,
    w.PublicationYear,
    w.PublicationDate,
    w.WorkType,
    w.WorkOpenAlexShortId,
    w.WorkDoi,
    w.WorkDisplayName,
    w.EvidenceSummary
FROM openalex.FacultyIdentityCandidateWork w
INNER JOIN openalex.Faculty f
    ON w.FacultyId = f.FacultyId
WHERE f.DisplayName LIKE @FacultyNameFilter
ORDER BY
    f.DisplayName,
    w.CandidateAuthorOpenAlexShortId,
    w.PublicationYear DESC,
    w.WorkDisplayName;

SELECT TOP (10)
    ir.IngestionRunId,
    ir.Status,
    ir.InstitutionFilter,
    ir.CreatedAtUtc,
    ir.FinishedAtUtc,
    ir.ApplicationVersion,
    ir.StartedBy,
    s.AuthorsProcessed,
    s.WorksProcessed,
    s.SummaryText
FROM openalex.IngestionRun ir
LEFT JOIN openalex.IngestionRunSummary s
    ON ir.IngestionRunId = s.IngestionRunId
WHERE ir.InstitutionFilter LIKE '%FacultyIdentityCandidateWorkExpansion%'
ORDER BY ir.IngestionRunId DESC;
