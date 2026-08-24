/*
V3E PATCH017B8 - Five-Faculty Cohort Registration Diagnostics
SQL Server 2016 compatible.

Purpose
-------
Verify that the named PATCH017B8 cohort contains exactly the five approved
faculty and is ready for cohort-scoped identity candidate generation.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

PRINT '1) PATCH017B8 cohort membership';
SELECT
    c.PilotOrder,
    c.FacultyId,
    f.DisplayName,
    f.DepartmentName,
    f.AppointmentTitle,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    c.IsEnabled,
    c.Notes,
    c.CreatedBy,
    c.CreatedAtUtc
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON f.FacultyId = c.FacultyId
WHERE c.CohortName = @CohortName
ORDER BY c.PilotOrder, c.FacultyId;

PRINT '2) Identity/publication readiness';
SELECT
    c.PilotOrder,
    c.FacultyId,
    f.DisplayName,
    HasOrcid = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NULL THEN 0 ELSE 1 END),
    HasClaimedOpenAlexAuthorId = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NULL THEN 0 ELSE 1 END),
    CurrentAcceptedAuthorIdentityCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = c.FacultyId
          AND d.DecisionStatus = 'Accepted'
          AND d.IsCurrent = 1
    ),
    ExistingPublicationCandidateCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationCandidate pc
        WHERE pc.FacultyId = c.FacultyId
    ),
    CurrentPublicationDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationDecision pd
        WHERE pd.FacultyId = c.FacultyId
          AND pd.IsCurrent = 1
    )
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON f.FacultyId = c.FacultyId
WHERE c.CohortName = @CohortName
  AND c.IsEnabled = 1
ORDER BY c.PilotOrder, c.FacultyId;

PRINT '3) Registration summary';
DECLARE @EnabledCount INT =
(
    SELECT COUNT(*)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);
DECLARE @DistinctFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);
DECLARE @DistinctOrderCount INT =
(
    SELECT COUNT(DISTINCT PilotOrder)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);
DECLARE @ActiveCount INT =
(
    SELECT COUNT(*)
    FROM openalex.PilotFacultyCohort c
    JOIN openalex.Faculty f ON f.FacultyId = c.FacultyId
    WHERE c.CohortName = @CohortName
      AND c.IsEnabled = 1
      AND f.IsActive = 1
);
DECLARE @StrongSeedCount INT =
(
    SELECT COUNT(*)
    FROM openalex.PilotFacultyCohort c
    JOIN openalex.Faculty f ON f.FacultyId = c.FacultyId
    WHERE c.CohortName = @CohortName
      AND c.IsEnabled = 1
      AND NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NOT NULL
);

SELECT
    CohortName = @CohortName,
    EnabledFacultyCount = @EnabledCount,
    DistinctFacultyCount = @DistinctFacultyCount,
    DistinctPilotOrderCount = @DistinctOrderCount,
    ActiveFacultyCount = @ActiveCount,
    StrongSeedFacultyCount = @StrongSeedCount,
    RegistrationStatus = CASE
        WHEN @EnabledCount = 5
         AND @DistinctFacultyCount = 5
         AND @DistinctOrderCount = 5
         AND @ActiveCount = 5
         AND @StrongSeedCount = 5
        THEN 'Ready'
        ELSE 'NotReady'
    END,
    SafetyStatus = 'CohortRegistered_NoMatchingDataChange',
    NextAction = CASE
        WHEN @EnabledCount = 5
         AND @DistinctFacultyCount = 5
         AND @DistinctOrderCount = 5
         AND @ActiveCount = 5
         AND @StrongSeedCount = 5
        THEN 'RunFacultyIdentityCandidateGenerationForV3E_PATCH017B8_FIVE_FACULTY'
        ELSE 'ResolveCohortRegistrationBeforeIdentityGeneration'
    END;
