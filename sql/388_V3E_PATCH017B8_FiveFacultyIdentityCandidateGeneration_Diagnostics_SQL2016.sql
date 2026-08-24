/*
V3E PATCH017B8 - Five-Faculty Identity Candidate Generation Diagnostics
SQL Server 2016 compatible.

Purpose
-------
Verify the first cohort-scoped FacultyIdentityCandidateGeneration run for
V3E_PATCH017B8_FIVE_FACULTY before work expansion is started.

READ ONLY.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestIngestionRunId BIGINT;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidate does not exist.', 16, 1);
    RETURN;
END;

SELECT TOP (1)
    @LatestIngestionRunId = r.IngestionRunId
FROM openalex.IngestionRun r
WHERE r.InstitutionFilter = N'FacultyIdentityCandidateGeneration; PilotCohortName=' + @CohortName
ORDER BY r.IngestionRunId DESC;

PRINT '1) Latest PATCH017B8 cohort identity-generation ingestion run';
SELECT
    r.IngestionRunId,
    r.RunMode,
    r.Status,
    r.InstitutionFilter,
    r.StartedBy,
    r.ApplicationVersion,
    r.StartedAtUtc,
    r.FinishedAtUtc,
    r.ErrorCount
FROM openalex.IngestionRun r
WHERE r.IngestionRunId = @LatestIngestionRunId;

PRINT '2) Ingestion summary';
SELECT
    s.IngestionRunId,
    s.AuthorsProcessed,
    s.WorksProcessed,
    s.SummaryText,
    s.CreatedAtUtc
FROM openalex.IngestionRunSummary s
WHERE s.IngestionRunId = @LatestIngestionRunId
ORDER BY s.CreatedAtUtc DESC;

PRINT '3) Candidate counts per cohort faculty';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    CandidateRowCount = COUNT(c.FacultyIdentityCandidateId),
    DistinctCandidateAuthorCount = COUNT(DISTINCT c.CandidateAuthorOpenAlexId),
    BestEvidenceScore = MAX(c.EvidenceScore),
    ClaimedOpenAlexIdFound = CONVERT(BIT, CASE WHEN EXISTS
    (
        SELECT 1
        FROM openalex.FacultyIdentityCandidate c2
        WHERE c2.FacultyId = f.FacultyId
          AND LOWER(REPLACE(REPLACE(c2.CandidateAuthorOpenAlexId, 'https://openalex.org/', ''), 'http://openalex.org/', '')) =
              LOWER(REPLACE(REPLACE(ISNULL(f.ClaimedOpenAlexAuthorId, ''), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
    ) THEN 1 ELSE 0 END),
    ExactOrcidCandidateFound = CONVERT(BIT, CASE WHEN EXISTS
    (
        SELECT 1
        FROM openalex.FacultyIdentityCandidate c3
        WHERE c3.FacultyId = f.FacultyId
          AND NULLIF(LTRIM(RTRIM(c3.CandidateOrcid)), '') IS NOT NULL
          AND LOWER(REPLACE(REPLACE(c3.CandidateOrcid, 'https://orcid.org/', ''), 'http://orcid.org/', '')) =
              LOWER(REPLACE(REPLACE(ISNULL(f.Orcid, ''), 'https://orcid.org/', ''), 'http://orcid.org/', ''))
    ) THEN 1 ELSE 0 END)
FROM openalex.PilotFacultyCohort pc
JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
LEFT JOIN openalex.FacultyIdentityCandidate c
    ON c.FacultyId = f.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
GROUP BY
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId
ORDER BY pc.PilotOrder;

PRINT '4) Candidate detail, strongest evidence first';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    FacultyDisplayName = f.DisplayName,
    c.FacultyIdentityCandidateId,
    c.CandidateAuthorOpenAlexId,
    c.CandidateAuthorOpenAlexShortId,
    c.CandidateDisplayName,
    c.CandidateOrcid,
    c.DiscoveryMethod,
    c.EvidenceScore,
    c.EvidenceStatus,
    c.WorksCount,
    c.CitedByCount,
    c.IngestionRunId
FROM openalex.PilotFacultyCohort pc
JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
JOIN openalex.FacultyIdentityCandidate c
    ON c.FacultyId = f.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY
    pc.PilotOrder,
    c.EvidenceScore DESC,
    c.WorksCount DESC,
    c.FacultyIdentityCandidateId;

PRINT '5) Pipeline safety / readiness summary';
SELECT
    EnabledFacultyCount =
    (
        SELECT COUNT(*)
        FROM openalex.PilotFacultyCohort
        WHERE CohortName = @CohortName
          AND IsEnabled = 1
    ),
    FacultyWithIdentityCandidates =
    (
        SELECT COUNT(DISTINCT c.FacultyId)
        FROM openalex.FacultyIdentityCandidate c
        JOIN openalex.PilotFacultyCohort pc
            ON pc.FacultyId = c.FacultyId
           AND pc.CohortName = @CohortName
           AND pc.IsEnabled = 1
    ),
    FacultyWithZeroIdentityCandidates =
    (
        SELECT COUNT(*)
        FROM openalex.PilotFacultyCohort pc
        WHERE pc.CohortName = @CohortName
          AND pc.IsEnabled = 1
          AND NOT EXISTS
          (
              SELECT 1
              FROM openalex.FacultyIdentityCandidate c
              WHERE c.FacultyId = pc.FacultyId
          )
    ),
    CurrentAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d
        JOIN openalex.PilotFacultyCohort pc
            ON pc.FacultyId = d.FacultyId
           AND pc.CohortName = @CohortName
           AND pc.IsEnabled = 1
        WHERE d.IsCurrent = 1
          AND d.DecisionStatus = 'Accepted'
    ),
    ExistingPublicationCandidateCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationCandidate c
        JOIN openalex.PilotFacultyCohort pc
            ON pc.FacultyId = c.FacultyId
           AND pc.CohortName = @CohortName
           AND pc.IsEnabled = 1
    ),
    CurrentPublicationDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationDecision d
        JOIN openalex.PilotFacultyCohort pc
            ON pc.FacultyId = d.FacultyId
           AND pc.CohortName = @CohortName
           AND pc.IsEnabled = 1
        WHERE d.IsCurrent = 1
    ),
    LatestRunStatus =
    (
        SELECT Status
        FROM openalex.IngestionRun
        WHERE IngestionRunId = @LatestIngestionRunId
    ),
    NextAction = CASE
        WHEN @LatestIngestionRunId IS NULL
            THEN 'IdentityGenerationRunNotFound'
        WHEN (SELECT Status FROM openalex.IngestionRun WHERE IngestionRunId = @LatestIngestionRunId) <> 'Completed'
            THEN 'InvestigateIdentityGenerationRun'
        WHEN EXISTS
        (
            SELECT 1
            FROM openalex.PilotFacultyCohort pc
            WHERE pc.CohortName = @CohortName
              AND pc.IsEnabled = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM openalex.FacultyIdentityCandidate c
                  WHERE c.FacultyId = pc.FacultyId
              )
        )
            THEN 'InvestigateFacultyWithZeroIdentityCandidates'
        ELSE 'ProceedToFacultyIdentityCandidateWorkExpansion'
    END;
