/*
V3D PATCH005 - Cohort-Aware Candidate Generation Diagnostic

Purpose:
- Confirm the selected PilotFacultyCohort name.
- Show the enabled active cohort faculty count and selected faculty IDs.
- Show author/publication candidate counts by faculty.
- Show cohort faculty with zero author candidates.

SQL Server 2016 notes:
- Uses FOR XML PATH list aggregation instead of STRING_AGG.
- Avoids DROP TABLE IF EXISTS, CONCAT_WS, TRIM, and other newer syntax.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';
DECLARE @SelectedFacultyIds NVARCHAR(MAX);
DECLARE @EnabledCohortFacultyCount BIGINT;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR(N'openalex.PilotFacultyCohort does not exist. Run the pilot cohort table setup before this diagnostic.', 10, 1);
    RETURN;
END;

SELECT @EnabledCohortFacultyCount = COUNT_BIG(*)
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
WHERE c.CohortName = @CohortName
  AND c.IsEnabled = 1
  AND f.IsActive = 1;

SELECT @SelectedFacultyIds = STUFF((
    SELECT N',' + CONVERT(NVARCHAR(20), f2.FacultyId)
    FROM openalex.PilotFacultyCohort c2
    JOIN openalex.Faculty f2
        ON c2.FacultyId = f2.FacultyId
    WHERE c2.CohortName = @CohortName
      AND c2.IsEnabled = 1
      AND f2.IsActive = 1
    ORDER BY c2.PilotOrder, f2.DisplayName
    FOR XML PATH(N''), TYPE
).value(N'.', N'NVARCHAR(MAX)'), 1, 1, N'');

PRINT 'PATCH005 selected cohort and faculty IDs';

SELECT
    SelectedCohortName = @CohortName,
    EnabledCohortFacultyCount = @EnabledCohortFacultyCount,
    FacultyProcessedExpected = @EnabledCohortFacultyCount,
    SelectedFacultyIds = ISNULL(@SelectedFacultyIds, N''),
    ValidationStatus =
        CASE
            WHEN @EnabledCohortFacultyCount = 15
            THEN N'READY: Candidate generation should process exactly 15 enabled active cohort faculty.'
            ELSE N'WARNING: Enabled active cohort faculty count is not exactly 15.'
        END;

PRINT 'PATCH005 candidate counts by cohort faculty';

SELECT
    c.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    AuthorCandidateCount = COUNT(mac.FacultyAuthorMatchCandidateId),
    PendingAuthorCandidateCount = SUM(CASE WHEN mac.CandidateStatus IN (N'PendingReview', N'NeedsMoreEvidence', N'AutoAcceptedCandidate') THEN 1 ELSE 0 END),
    AcceptedAuthorDecisionCount =
    (
        SELECT COUNT_BIG(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = N'Accepted'
    ),
    PublicationCandidateCount =
    (
        SELECT COUNT_BIG(*)
        FROM openalex.FacultyPublicationCandidate pc
        WHERE pc.FacultyId = f.FacultyId
    )
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
LEFT JOIN openalex.FacultyAuthorMatchCandidate mac
    ON f.FacultyId = mac.FacultyId
WHERE c.CohortName = @CohortName
  AND c.IsEnabled = 1
  AND f.IsActive = 1
GROUP BY
    c.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId
ORDER BY c.PilotOrder, f.DisplayName;

PRINT 'PATCH005 faculty with zero author candidates';

SELECT
    c.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.PrimaryEmail,
    f.DepartmentName
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
WHERE c.CohortName = @CohortName
  AND c.IsEnabled = 1
  AND f.IsActive = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM openalex.FacultyAuthorMatchCandidate mac
      WHERE mac.FacultyId = f.FacultyId
  )
ORDER BY c.PilotOrder, f.DisplayName;

PRINT 'PATCH005 recent candidate generation runs';

SELECT TOP 10
    r.IngestionRunId,
    r.RunMode,
    r.Status,
    r.InstitutionFilter,
    r.MaxRecordsPerEntity,
    r.StartedBy,
    r.ApplicationVersion,
    r.StartedAtUtc,
    r.FinishedAtUtc,
    s.AuthorsProcessed,
    s.WorksProcessed,
    s.SummaryText
FROM openalex.IngestionRun r
LEFT JOIN openalex.IngestionRunSummary s
    ON r.IngestionRunId = s.IngestionRunId
WHERE r.InstitutionFilter LIKE N'FacultyAuthorCandidateGeneration%'
   OR r.InstitutionFilter LIKE N'FacultyPublicationCandidateGeneration%'
ORDER BY r.IngestionRunId DESC;

PRINT 'PATCH005 potential name-only author hints for zero-candidate faculty';

SELECT TOP 100
    c.PilotOrder,
    f.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    a.AuthorId,
    a.DisplayName AS OpenAlexAuthorDisplayName,
    a.OpenAlexId AS AuthorOpenAlexId,
    a.Orcid AS AuthorOrcid,
    a.WorksCount,
    a.CitedByCount
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
JOIN openalex.Author a
    ON
    (
        a.DisplayName LIKE N'%' + PARSENAME(REPLACE(f.DisplayName, N' ', N'.'), 1) + N'%'
        OR
        (f.ClaimedOpenAlexAuthorId IS NOT NULL AND a.OpenAlexId = f.ClaimedOpenAlexAuthorId)
        OR
        (f.Orcid IS NOT NULL AND a.Orcid = f.Orcid)
    )
WHERE c.CohortName = @CohortName
  AND c.IsEnabled = 1
  AND f.IsActive = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM openalex.FacultyAuthorMatchCandidate mac
      WHERE mac.FacultyId = f.FacultyId
  )
ORDER BY c.PilotOrder, a.WorksCount DESC, a.CitedByCount DESC;
