/*
V3E PATCH017B8 - Controlled Five-Faculty Cohort Registration
SQL Server 2016 compatible.

Purpose
-------
Register the five faculty approved by the PATCH017B8 preflight into the named
pilot cohort V3E_PATCH017B8_FIVE_FACULTY.

Safety
------
- Changes ONLY openalex.PilotFacultyCohort.
- Does not alter faculty, identity candidates/decisions, publication candidates,
  or publication decisions.
- Does not disable or modify memberships in any other cohort.
- Validates the exact five approved FacultyIds before writing.
- Idempotent for the named cohort.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @CreatedBy NVARCHAR(512) = N'OpenAlex.V3E.PATCH017B8';
DECLARE @ConfirmRegistration BIT = 1;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

DECLARE @Expected TABLE
(
    PilotOrder INT NOT NULL PRIMARY KEY,
    FacultyId BIGINT NOT NULL UNIQUE,
    ExpectedDisplayName NVARCHAR(512) NOT NULL
);

INSERT INTO @Expected(PilotOrder, FacultyId, ExpectedDisplayName)
VALUES
    (1, 15, N'David C. Slawson'),
    (2, 17, N'Edward H. Egelman'),
    (3, 14, N'J. David Castle'),
    (4, 13, N'James E. Casanova'),
    (5,  6, N'Janet Cross');

PRINT '1) PATCH017B8 registration validation';
SELECT
    e.PilotOrder,
    e.FacultyId,
    ExpectedDisplayName = e.ExpectedDisplayName,
    CurrentDisplayName = f.DisplayName,
    f.DepartmentName,
    f.AppointmentTitle,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    f.IsActive,
    NameMatchesPreflight = CONVERT(BIT, CASE WHEN f.DisplayName = e.ExpectedDisplayName THEN 1 ELSE 0 END),
    HasOrcid = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NULL THEN 0 ELSE 1 END),
    HasClaimedOpenAlexAuthorId = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NULL THEN 0 ELSE 1 END)
FROM @Expected e
LEFT JOIN openalex.Faculty f
    ON f.FacultyId = e.FacultyId
ORDER BY e.PilotOrder;

IF EXISTS
(
    SELECT 1
    FROM @Expected e
    LEFT JOIN openalex.Faculty f
        ON f.FacultyId = e.FacultyId
    WHERE f.FacultyId IS NULL
       OR f.IsActive <> 1
       OR f.DisplayName <> e.ExpectedDisplayName
       OR NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NULL
)
BEGIN
    RAISERROR('PATCH017B8 registration validation failed. One or more approved faculty are missing, inactive, renamed, or no longer have both ORCID and ClaimedOpenAlexAuthorId.', 16, 1);
    RETURN;
END;

IF EXISTS
(
    SELECT 1
    FROM openalex.PilotFacultyCohort c
    WHERE c.CohortName = @CohortName
      AND NOT EXISTS
      (
          SELECT 1
          FROM @Expected e
          WHERE e.FacultyId = c.FacultyId
      )
)
BEGIN
    RAISERROR('The PATCH017B8 cohort name already contains an unexpected faculty member. No registration changes were made.', 16, 1);
    RETURN;
END;

PRINT '2) Existing memberships for the approved five (all cohorts)';
SELECT
    c.CohortName,
    c.FacultyId,
    f.DisplayName,
    c.PilotOrder,
    c.IsEnabled,
    c.Notes,
    c.CreatedBy,
    c.CreatedAtUtc
FROM openalex.PilotFacultyCohort c
JOIN @Expected e
    ON e.FacultyId = c.FacultyId
JOIN openalex.Faculty f
    ON f.FacultyId = c.FacultyId
ORDER BY c.FacultyId, c.CohortName;

IF @ConfirmRegistration <> 1
BEGIN
    SELECT
        SafetyStatus = 'PreviewOnly_NoCohortChange',
        NextAction = 'SetConfirmRegistrationTo1ThenRerun';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE c
    SET
        c.PilotOrder = e.PilotOrder,
        c.IsEnabled = 1,
        c.Notes = N'PATCH017B8 controlled five-faculty expansion cohort. Approved from preflight 385.',
        c.CreatedBy = COALESCE(c.CreatedBy, @CreatedBy)
    FROM openalex.PilotFacultyCohort c
    JOIN @Expected e
        ON e.FacultyId = c.FacultyId
    WHERE c.CohortName = @CohortName;

    DECLARE @RowsUpdated INT = @@ROWCOUNT;

    INSERT INTO openalex.PilotFacultyCohort
    (
        CohortName,
        FacultyId,
        PilotOrder,
        IsEnabled,
        Notes,
        CreatedBy,
        CreatedAtUtc
    )
    SELECT
        @CohortName,
        e.FacultyId,
        e.PilotOrder,
        1,
        N'PATCH017B8 controlled five-faculty expansion cohort. Approved from preflight 385.',
        @CreatedBy,
        SYSUTCDATETIME()
    FROM @Expected e
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM openalex.PilotFacultyCohort c WITH (UPDLOCK, HOLDLOCK)
        WHERE c.CohortName = @CohortName
          AND c.FacultyId = e.FacultyId
    );

    DECLARE @RowsInserted INT = @@ROWCOUNT;

    DECLARE @EnabledCount INT =
    (
        SELECT COUNT(*)
        FROM openalex.PilotFacultyCohort c
        WHERE c.CohortName = @CohortName
          AND c.IsEnabled = 1
    );

    DECLARE @DistinctFacultyCount INT =
    (
        SELECT COUNT(DISTINCT c.FacultyId)
        FROM openalex.PilotFacultyCohort c
        WHERE c.CohortName = @CohortName
          AND c.IsEnabled = 1
    );

    DECLARE @DistinctOrderCount INT =
    (
        SELECT COUNT(DISTINCT c.PilotOrder)
        FROM openalex.PilotFacultyCohort c
        WHERE c.CohortName = @CohortName
          AND c.IsEnabled = 1
    );

    IF @EnabledCount <> 5 OR @DistinctFacultyCount <> 5 OR @DistinctOrderCount <> 5
    BEGIN
        RAISERROR('PATCH017B8 cohort verification failed inside the transaction. Expected exactly five enabled faculty with five distinct PilotOrder values.', 16, 1);
    END;

    COMMIT TRANSACTION;

    PRINT '3) PATCH017B8 cohort registration result';
    SELECT
        CohortName = @CohortName,
        RowsInserted = @RowsInserted,
        RowsUpdated = @RowsUpdated,
        EnabledFacultyCount = @EnabledCount,
        DistinctFacultyCount = @DistinctFacultyCount,
        DistinctPilotOrderCount = @DistinctOrderCount,
        RegistrationStatus = 'Completed';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

PRINT '4) Registered cohort';
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

PRINT '5) Safety summary';
SELECT
    CohortName = @CohortName,
    EnabledFacultyCount =
    (
        SELECT COUNT(*)
        FROM openalex.PilotFacultyCohort
        WHERE CohortName = @CohortName
          AND IsEnabled = 1
    ),
    SafetyStatus = 'ControlledRegistration_PilotFacultyCohortOnly',
    OtherCohortsUntouched = 1,
    MatchingDataUntouched = 1,
    NextAction = 'Run387RegistrationDiagnosticsThenLaunchCohortIdentityCandidateGeneration';
