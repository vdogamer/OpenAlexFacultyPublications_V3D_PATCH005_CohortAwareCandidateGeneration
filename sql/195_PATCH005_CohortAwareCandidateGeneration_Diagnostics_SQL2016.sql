/*
PATCH005 Cohort-Aware Candidate Generation Diagnostics
SQL Server 2016-safe: no STRING_AGG, no DROP IF EXISTS, no CONCAT_WS, no TRIM.

Change @PilotCohortName only if you are testing a different cohort.
*/

SET NOCOUNT ON;

DECLARE @PilotCohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';

PRINT 'PATCH005 Cohort-Aware Candidate Generation Diagnostics';
PRINT 'Selected cohort name:';
SELECT @PilotCohortName AS SelectedCohortName;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist. Run the pilot cohort setup script first.', 16, 1);
    RETURN;
END;

;WITH SelectedCohortFaculty AS
(
    SELECT
        pc.CohortName,
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
        pc.CohortName,
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
    FacultyId,
    DisplayName,
    PilotOrder,
    IsEnabled,
    IsActive
FROM SelectedCohortFaculty
ORDER BY PilotOrder, DisplayName, FacultyId;

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
AuthorCandidateCounts AS
(
    SELECT
        FacultyId,
        COUNT(*) AS AuthorCandidateCount
    FROM openalex.FacultyAuthorMatchCandidate
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    ISNULL(acc.AuthorCandidateCount, 0) AS AuthorCandidateCount
FROM SelectedCohortFaculty scf
LEFT JOIN AuthorCandidateCounts acc
    ON scf.FacultyId = acc.FacultyId
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
),
AuthorCandidateCounts AS
(
    SELECT
        FacultyId,
        COUNT(*) AS AuthorCandidateCount
    FROM openalex.FacultyAuthorMatchCandidate
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    0 AS AuthorCandidateCount
FROM SelectedCohortFaculty scf
LEFT JOIN AuthorCandidateCounts acc
    ON scf.FacultyId = acc.FacultyId
WHERE ISNULL(acc.AuthorCandidateCount, 0) = 0
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
),
PublicationCandidateCounts AS
(
    SELECT
        FacultyId,
        COUNT(*) AS PublicationCandidateCount
    FROM openalex.FacultyPublicationCandidate
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    ISNULL(pcc.PublicationCandidateCount, 0) AS PublicationCandidateCount
FROM SelectedCohortFaculty scf
LEFT JOIN PublicationCandidateCounts pcc
    ON scf.FacultyId = pcc.FacultyId
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
WHERE ir.InstitutionFilter LIKE '%FacultyAuthorCandidateGeneration%'
   OR ir.InstitutionFilter LIKE '%FacultyPublicationCandidateGeneration%'
ORDER BY ir.IngestionRunId DESC;
