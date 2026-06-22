/*
V3E PATCH001A - Grouped Faculty Identity Candidate Diagnostics
SQL Server 2016-safe.

Purpose:
  Show one row per Faculty + candidate OpenAlex author ID so repeated discovery methods are easier to interpret.
*/

SET NOCOUNT ON;

DECLARE @PilotCohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';

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

PRINT 'V3E PATCH001A Grouped Faculty Identity Candidate Diagnostics';
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
CandidateRows AS
(
    SELECT
        scf.FacultyId,
        scf.DisplayName AS FacultyName,
        scf.PilotOrder,
        fic.CandidateAuthorOpenAlexId,
        fic.CandidateAuthorOpenAlexShortId,
        fic.CandidateDisplayName,
        fic.CandidateOrcid,
        fic.WorksCount,
        fic.CitedByCount,
        fic.DiscoveryMethod,
        fic.EvidenceScore,
        fic.EvidenceStatus,
        CASE
            WHEN CHARINDEX('CandidateType=', fic.EvidenceSummary) > 0
             AND CHARINDEX(';', fic.EvidenceSummary, CHARINDEX('CandidateType=', fic.EvidenceSummary)) > CHARINDEX('CandidateType=', fic.EvidenceSummary)
            THEN SUBSTRING(
                    fic.EvidenceSummary,
                    CHARINDEX('CandidateType=', fic.EvidenceSummary) + LEN('CandidateType='),
                    CHARINDEX(';', fic.EvidenceSummary, CHARINDEX('CandidateType=', fic.EvidenceSummary)) - (CHARINDEX('CandidateType=', fic.EvidenceSummary) + LEN('CandidateType='))
                 )
            ELSE '(not tagged)'
        END AS CandidateType
    FROM SelectedCohortFaculty scf
    INNER JOIN openalex.FacultyIdentityCandidate fic
        ON scf.FacultyId = fic.FacultyId
),
GroupedCandidates AS
(
    SELECT
        FacultyId,
        FacultyName,
        PilotOrder,
        CandidateAuthorOpenAlexId,
        CandidateAuthorOpenAlexShortId,
        CandidateDisplayName,
        CandidateOrcid,
        MAX(WorksCount) AS WorksCount,
        MAX(CitedByCount) AS CitedByCount,
        MAX(EvidenceScore) AS BestEvidenceScore,
        COUNT(*) AS DiscoveryMethodCount,
        MAX(CASE WHEN EvidenceStatus = 'StrongIdentifierCandidate' THEN 5
                 WHEN EvidenceStatus = 'CandidateForReview' THEN 4
                 WHEN EvidenceStatus = 'PossibleNameCandidate' THEN 3
                 WHEN EvidenceStatus = 'WeakNameCandidate' THEN 2
                 ELSE 1 END) AS BestEvidenceStatusRank,
        MAX(CASE WHEN CandidateType = 'PrimaryIdentityCandidate' THEN 4
                 WHEN CandidateType = 'PossibleProfileFragment' THEN 3
                 WHEN CandidateType = 'WeakPossibleMatch' THEN 2
                 WHEN CandidateType = 'LikelyFalsePositive' THEN 1
                 ELSE 0 END) AS CandidateTypeRank
    FROM CandidateRows
    GROUP BY
        FacultyId,
        FacultyName,
        PilotOrder,
        CandidateAuthorOpenAlexId,
        CandidateAuthorOpenAlexShortId,
        CandidateDisplayName,
        CandidateOrcid
)
SELECT
    gc.FacultyId,
    gc.FacultyName,
    gc.CandidateAuthorOpenAlexShortId,
    gc.CandidateDisplayName,
    gc.CandidateOrcid,
    gc.WorksCount,
    gc.CitedByCount,
    gc.BestEvidenceScore,
    CASE gc.BestEvidenceStatusRank
        WHEN 5 THEN 'StrongIdentifierCandidate'
        WHEN 4 THEN 'CandidateForReview'
        WHEN 3 THEN 'PossibleNameCandidate'
        WHEN 2 THEN 'WeakNameCandidate'
        ELSE 'NeedsMoreEvidence'
    END AS BestEvidenceStatus,
    CASE gc.CandidateTypeRank
        WHEN 4 THEN 'PrimaryIdentityCandidate'
        WHEN 3 THEN 'PossibleProfileFragment'
        WHEN 2 THEN 'WeakPossibleMatch'
        WHEN 1 THEN 'LikelyFalsePositive'
        ELSE '(not tagged)'
    END AS BestCandidateType,
    gc.DiscoveryMethodCount,
    STUFF
    (
        (
            SELECT DISTINCT ', ' + cr2.DiscoveryMethod
            FROM CandidateRows cr2
            WHERE cr2.FacultyId = gc.FacultyId
              AND cr2.CandidateAuthorOpenAlexId = gc.CandidateAuthorOpenAlexId
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'),
        1,
        2,
        ''
    ) AS DiscoveryMethods
FROM GroupedCandidates gc
ORDER BY
    gc.PilotOrder,
    gc.FacultyName,
    gc.BestEvidenceScore DESC,
    gc.DiscoveryMethodCount DESC,
    gc.CandidateDisplayName;

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
