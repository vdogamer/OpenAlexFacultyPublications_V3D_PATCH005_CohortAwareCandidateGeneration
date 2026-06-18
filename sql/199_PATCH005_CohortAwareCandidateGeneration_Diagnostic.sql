/*
V3D PATCH005 — Cohort-Aware Candidate Generation Diagnostic

Purpose:
- Confirm that the candidate generator should process the exact 15 enabled faculty in openalex.PilotFacultyCohort.
- Show which cohort faculty have author candidates and which do not.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';

PRINT 'PATCH005 cohort readiness';

SELECT
    CohortName = @CohortName,
    EnabledFacultyCount =
    (
        SELECT COUNT_BIG(*)
        FROM openalex.PilotFacultyCohort c
        JOIN openalex.Faculty f
            ON c.FacultyId = f.FacultyId
        WHERE c.CohortName = @CohortName
          AND c.IsEnabled = 1
          AND f.IsActive = 1
    ),
    ValidationStatus =
    CASE
        WHEN
        (
            SELECT COUNT_BIG(*)
            FROM openalex.PilotFacultyCohort c
            JOIN openalex.Faculty f
                ON c.FacultyId = f.FacultyId
            WHERE c.CohortName = @CohortName
              AND c.IsEnabled = 1
              AND f.IsActive = 1
        ) = 15
        THEN N'READY: Candidate generation should process exactly 15 active cohort faculty.'
        ELSE N'WARNING: The enabled active cohort count is not exactly 15.'
    END;

PRINT 'Cohort faculty candidate counts';

SELECT
    c.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    CandidateCount = COUNT(mac.FacultyAuthorMatchCandidateId),
    PendingCandidateCount = SUM(CASE WHEN mac.CandidateStatus IN (N'PendingReview', N'NeedsMoreEvidence', N'AutoAcceptedCandidate') THEN 1 ELSE 0 END),
    AcceptedDecisionCount =
    (
        SELECT COUNT_BIG(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = N'Accepted'
    ),
    RejectedDecisionCount =
    (
        SELECT COUNT_BIG(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = N'Rejected'
    )
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
LEFT JOIN openalex.FacultyAuthorMatchCandidate mac
    ON f.FacultyId = mac.FacultyId
WHERE c.CohortName = @CohortName
  AND c.IsEnabled = 1
GROUP BY
    c.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId
ORDER BY c.PilotOrder;

PRINT 'Recent candidate generation runs';

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

PRINT 'Potential name-only author table hints for cohort faculty';

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
ORDER BY c.PilotOrder, a.WorksCount DESC, a.CitedByCount DESC;
