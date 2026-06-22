/*
V3E PATCH001 - Faculty Identity Candidate Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

DECLARE @PilotCohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';

PRINT 'V3E PATCH001 Faculty Identity Candidate Diagnostics';
SELECT @PilotCohortName AS SelectedCohortName;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidate does not exist. Run sql/200_V3E_PATCH001_FacultyIdentityCandidate.sql first.', 16, 1);
    RETURN;
END;

;WITH SelectedCohortFaculty AS
(
    SELECT
        pc.FacultyId,
        pc.PilotOrder,
        pc.IsEnabled,
        f.DisplayName,
        f.IsActive
    FROM openalex.PilotFacultyCohort pc
    LEFT JOIN openalex.Faculty f
        ON pc.FacultyId = f.FacultyId
    WHERE pc.CohortName = @PilotCohortName
)
SELECT
    @PilotCohortName AS SelectedCohortName,
    COUNT(*) AS CohortRows,
    SUM(CASE WHEN IsEnabled = 1 THEN 1 ELSE 0 END) AS EnabledCohortRows,
    SUM(CASE WHEN IsEnabled = 1 AND IsActive = 1 THEN 1 ELSE 0 END) AS EnabledActiveCohortFacultyCount
FROM SelectedCohortFaculty;

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
        COUNT(*) AS IdentityCandidateRows,
        COUNT(DISTINCT CandidateAuthorOpenAlexId) AS DistinctAuthorCandidates
    FROM openalex.FacultyIdentityCandidate
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    ISNULL(cc.IdentityCandidateRows, 0) AS IdentityCandidateRows,
    ISNULL(cc.DistinctAuthorCandidates, 0) AS DistinctAuthorCandidates
FROM SelectedCohortFaculty scf
LEFT JOIN CandidateCounts cc
    ON scf.FacultyId = cc.FacultyId
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
    scf.DisplayName,
    fic.DiscoveryMethod,
    fic.CandidateAuthorOpenAlexShortId,
    fic.CandidateDisplayName,
    fic.CandidateOrcid,
    fic.WorksCount,
    fic.CitedByCount,
    fic.EvidenceScore,
    fic.EvidenceStatus,
    fic.EvidenceSummary,
    fic.UpdatedAtUtc
FROM SelectedCohortFaculty scf
INNER JOIN openalex.FacultyIdentityCandidate fic
    ON scf.FacultyId = fic.FacultyId
ORDER BY
    scf.PilotOrder,
    scf.DisplayName,
    fic.EvidenceScore DESC,
    fic.CandidateDisplayName,
    fic.DiscoveryMethod;

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
    scf.DisplayName
FROM SelectedCohortFaculty scf
LEFT JOIN openalex.FacultyIdentityCandidate fic
    ON scf.FacultyId = fic.FacultyId
WHERE fic.FacultyIdentityCandidateId IS NULL
ORDER BY scf.PilotOrder, scf.DisplayName, scf.FacultyId;

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
WHERE ir.InstitutionFilter LIKE '%FacultyIdentityCandidateGeneration%'
ORDER BY ir.IngestionRunId DESC;
