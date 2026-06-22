/*
V3E PATCH004 - Faculty Identity Candidate Summary Diagnostics
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

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidateSummary does not exist. Run sql/230_V3E_PATCH004_FacultyIdentityCandidateSummary.sql first.', 16, 1);
    RETURN;
END;

PRINT 'V3E PATCH004 Faculty Identity Candidate Summary Diagnostics';
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
SummaryCounts AS
(
    SELECT
        FacultyId,
        COUNT(*) AS SummaryRows,
        SUM(CASE WHEN IdentityReviewTriageStatus = 'LikelyIdentity' THEN 1 ELSE 0 END) AS LikelyIdentityRows,
        SUM(CASE WHEN IdentityReviewTriageStatus = 'PossibleSplitProfile' THEN 1 ELSE 0 END) AS PossibleSplitProfileRows,
        SUM(CASE WHEN IdentityReviewTriageStatus = 'NeedsManualReview' THEN 1 ELSE 0 END) AS NeedsManualReviewRows,
        SUM(CASE WHEN IdentityReviewTriageStatus = 'WeakCandidate' THEN 1 ELSE 0 END) AS WeakCandidateRows,
        SUM(CASE WHEN IdentityReviewTriageStatus = 'LikelyFalsePositive' THEN 1 ELSE 0 END) AS LikelyFalsePositiveRows
    FROM openalex.FacultyIdentityCandidateSummary
    GROUP BY FacultyId
)
SELECT
    scf.FacultyId,
    scf.DisplayName,
    ISNULL(sc.SummaryRows, 0) AS SummaryRows,
    ISNULL(sc.LikelyIdentityRows, 0) AS LikelyIdentityRows,
    ISNULL(sc.PossibleSplitProfileRows, 0) AS PossibleSplitProfileRows,
    ISNULL(sc.NeedsManualReviewRows, 0) AS NeedsManualReviewRows,
    ISNULL(sc.WeakCandidateRows, 0) AS WeakCandidateRows,
    ISNULL(sc.LikelyFalsePositiveRows, 0) AS LikelyFalsePositiveRows
FROM SelectedCohortFaculty scf
LEFT JOIN SummaryCounts sc
    ON scf.FacultyId = sc.FacultyId
ORDER BY scf.PilotOrder, scf.DisplayName, scf.FacultyId;

SELECT
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority,
    s.BestCandidateType,
    s.BestIdentityEvidenceScore,
    s.BestIdentityEvidenceStatus,
    s.DiscoveryMethodCount,
    s.DiscoveryMethods,
    s.DistinctExpandedWorks,
    s.ScoredWorkRows,
    s.StrongWorkEvidenceRows,
    s.ModerateWorkEvidenceRows,
    s.WeakWorkEvidenceRows,
    s.ConflictingWorkEvidenceRows,
    s.AverageWorkEvidenceScore,
    s.BestWorkEvidenceScore,
    s.EarliestPublicationYear,
    s.LatestPublicationYear,
    s.SummaryText
FROM openalex.FacultyIdentityCandidateSummary s
WHERE s.FacultyDisplayName LIKE @FacultyNameFilter
ORDER BY
    s.ReviewPriority,
    s.BestIdentityEvidenceScore DESC,
    s.BestWorkEvidenceScore DESC,
    s.CandidateDisplayName;

SELECT TOP (100)
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority,
    s.BestIdentityEvidenceScore,
    s.BestWorkEvidenceScore,
    s.DistinctExpandedWorks,
    s.StrongWorkEvidenceRows,
    s.ModerateWorkEvidenceRows,
    s.ConflictingWorkEvidenceRows,
    s.SummaryText
FROM openalex.FacultyIdentityCandidateSummary s
ORDER BY
    s.FacultyDisplayName,
    s.ReviewPriority,
    s.BestIdentityEvidenceScore DESC,
    s.BestWorkEvidenceScore DESC;

SELECT TOP (10)
    ir.IngestionRunId,
    ir.Status,
    ir.InstitutionFilter,
    ir.CreatedAtUtc,
    ir.FinishedAtUtc,
    ir.ApplicationVersion,
    ir.StartedBy,
    rs.AuthorsProcessed,
    rs.WorksProcessed,
    rs.SummaryText
FROM openalex.IngestionRun ir
LEFT JOIN openalex.IngestionRunSummary rs
    ON ir.IngestionRunId = rs.IngestionRunId
WHERE ir.InstitutionFilter LIKE '%FacultyIdentityCandidateSummaryGeneration%'
ORDER BY ir.IngestionRunId DESC;
