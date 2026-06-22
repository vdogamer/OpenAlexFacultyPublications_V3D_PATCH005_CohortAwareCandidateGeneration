/*
V3E PATCH003 - Identity Work Evidence Scoring Diagnostics
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

IF OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityWorkEvidenceScore does not exist. Run sql/220_V3E_PATCH003_FacultyIdentityWorkEvidenceScore.sql first.', 16, 1);
    RETURN;
END;

PRINT 'V3E PATCH003 Identity Work Evidence Scoring Diagnostics';
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
ScoreCounts AS
(
    SELECT
        FacultyId,
        COUNT(*) AS ScoredWorkRows,
        COUNT(DISTINCT WorkOpenAlexId) AS DistinctScoredWorks,
        COUNT(DISTINCT CandidateAuthorOpenAlexId) AS CandidateAuthorsScored,
        SUM(CASE WHEN WorkEvidenceStatus = 'StrongWorkEvidence' THEN 1 ELSE 0 END) AS StrongWorkEvidenceRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'ModerateWorkEvidence' THEN 1 ELSE 0 END) AS ModerateWorkEvidenceRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'WeakWorkEvidence' THEN 1 ELSE 0 END) AS WeakWorkEvidenceRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'ConflictingWorkEvidence' THEN 1 ELSE 0 END) AS ConflictingWorkEvidenceRows,
        MIN(PublicationYear) AS EarliestPublicationYear,
        MAX(PublicationYear) AS LatestPublicationYear
    FROM openalex.FacultyIdentityWorkEvidenceScore
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    ISNULL(sc.CandidateAuthorsScored, 0) AS CandidateAuthorsScored,
    ISNULL(sc.ScoredWorkRows, 0) AS ScoredWorkRows,
    ISNULL(sc.DistinctScoredWorks, 0) AS DistinctScoredWorks,
    ISNULL(sc.StrongWorkEvidenceRows, 0) AS StrongWorkEvidenceRows,
    ISNULL(sc.ModerateWorkEvidenceRows, 0) AS ModerateWorkEvidenceRows,
    ISNULL(sc.WeakWorkEvidenceRows, 0) AS WeakWorkEvidenceRows,
    ISNULL(sc.ConflictingWorkEvidenceRows, 0) AS ConflictingWorkEvidenceRows,
    sc.EarliestPublicationYear,
    sc.LatestPublicationYear
FROM SelectedCohortFaculty scf
LEFT JOIN ScoreCounts sc
    ON scf.FacultyId = sc.FacultyId
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
    s.CandidateAuthorOpenAlexShortId,
    MAX(s.MatchedAuthorDisplayName) AS MatchedAuthorDisplayName,
    COUNT(*) AS ScoredWorkRows,
    COUNT(DISTINCT s.WorkOpenAlexId) AS DistinctScoredWorks,
    AVG(s.OverallWorkEvidenceScore) AS AverageWorkEvidenceScore,
    MAX(s.OverallWorkEvidenceScore) AS BestWorkEvidenceScore,
    SUM(CASE WHEN s.WorkEvidenceStatus = 'StrongWorkEvidence' THEN 1 ELSE 0 END) AS StrongRows,
    SUM(CASE WHEN s.WorkEvidenceStatus = 'ModerateWorkEvidence' THEN 1 ELSE 0 END) AS ModerateRows,
    SUM(CASE WHEN s.WorkEvidenceStatus = 'WeakWorkEvidence' THEN 1 ELSE 0 END) AS WeakRows,
    SUM(CASE WHEN s.WorkEvidenceStatus = 'ConflictingWorkEvidence' THEN 1 ELSE 0 END) AS ConflictingRows,
    MIN(s.PublicationYear) AS EarliestPublicationYear,
    MAX(s.PublicationYear) AS LatestPublicationYear
FROM SelectedCohortFaculty scf
INNER JOIN openalex.FacultyIdentityWorkEvidenceScore s
    ON scf.FacultyId = s.FacultyId
GROUP BY
    scf.FacultyId,
    scf.DisplayName,
    scf.PilotOrder,
    s.CandidateAuthorOpenAlexShortId
ORDER BY
    scf.PilotOrder,
    scf.DisplayName,
    MAX(s.OverallWorkEvidenceScore) DESC,
    AVG(s.OverallWorkEvidenceScore) DESC,
    s.CandidateAuthorOpenAlexShortId;

SELECT TOP (100)
    f.FacultyId,
    f.DisplayName AS FacultyName,
    s.CandidateAuthorOpenAlexShortId,
    s.MatchedAuthorDisplayName,
    s.WorkEvidenceStatus,
    s.OverallWorkEvidenceScore,
    s.NameEvidenceScore,
    s.InstitutionEvidenceScore,
    s.DoiEvidenceScore,
    s.PublicationYearEvidenceScore,
    s.PublicationYear,
    s.WorkOpenAlexShortId,
    s.WorkDisplayName,
    s.EvidenceSummary
FROM openalex.FacultyIdentityWorkEvidenceScore s
INNER JOIN openalex.Faculty f
    ON s.FacultyId = f.FacultyId
WHERE f.DisplayName LIKE @FacultyNameFilter
ORDER BY
    f.DisplayName,
    s.CandidateAuthorOpenAlexShortId,
    s.OverallWorkEvidenceScore DESC,
    s.PublicationYear DESC,
    s.WorkDisplayName;

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
WHERE ir.InstitutionFilter LIKE '%FacultyIdentityWorkEvidenceScoring%'
ORDER BY ir.IngestionRunId DESC;
