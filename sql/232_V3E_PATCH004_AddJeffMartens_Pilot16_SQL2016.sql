/*
V3E PATCH004 - Add Jeff Martens as 16th Pilot Faculty Member
SQL Server 2016-safe.

Purpose:
  1. Add Jeff/Jeffrey Martens to openalex.Faculty if he does not already exist.
  2. Create a new cohort named 'Pilot Expansion 16 Faculty' by copying enabled/disabled rows from 'Pilot Expansion 15 Faculty'.
  3. Add Jeff Martens to the new cohort as PilotOrder = 16.

Important:
  This script does NOT insert accepted OpenAlex author decisions.
  This script does NOT insert accepted publications.
  It only adds a faculty/cohort member so V3E can discover, expand, score, and summarize evidence.

After running this script, update appsettings.json:
  CandidateGeneration:PilotCohortName = Pilot Expansion 16 Faculty
  CandidateGeneration:MaxFacultyToProcess = 16
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExistingCohortName NVARCHAR(200) = N'Pilot Expansion 15 Faculty';
DECLARE @NewCohortName NVARCHAR(200) = N'Pilot Expansion 16 Faculty';
DECLARE @PilotOrder INT = 16;

-- Use Jeffrey for better OpenAlex name-discovery coverage. Change to N'Jeff Martens' only if the local faculty display name must be informal.
DECLARE @DisplayName NVARCHAR(512) = N'Jeffrey Martens';
DECLARE @AlternateDisplayName NVARCHAR(512) = N'Jeff Martens';
DECLARE @FirstName NVARCHAR(200) = N'Jeffrey';
DECLARE @LastName NVARCHAR(200) = N'Martens';
DECLARE @PrimaryEmail NVARCHAR(320) = NULL;
DECLARE @DepartmentName NVARCHAR(512) = NULL;
DECLARE @AppointmentTitle NVARCHAR(512) = NULL;
DECLARE @Orcid NVARCHAR(256) = NULL;
DECLARE @ClaimedOpenAlexAuthorId NVARCHAR(128) = NULL;

DECLARE @FacultyId BIGINT;
DECLARE @sql NVARCHAR(MAX);
DECLARE @params NVARCHAR(MAX);

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'openalex.Faculty', N'FacultyId') IS NULL
BEGIN
    RAISERROR('openalex.Faculty.FacultyId does not exist.', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'openalex.PilotFacultyCohort', N'CohortName') IS NULL
   OR COL_LENGTH(N'openalex.PilotFacultyCohort', N'FacultyId') IS NULL
   OR COL_LENGTH(N'openalex.PilotFacultyCohort', N'PilotOrder') IS NULL
   OR COL_LENGTH(N'openalex.PilotFacultyCohort', N'IsEnabled') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort must include CohortName, FacultyId, PilotOrder, and IsEnabled.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

/*
Find existing faculty row.
Prefer exact display-name matches first, then first/last name matches when those columns exist.
*/
SET @sql = N'
SELECT TOP (1) @FacultyIdOut = FacultyId
FROM openalex.Faculty
WHERE 1 = 0';

IF COL_LENGTH(N'openalex.Faculty', N'DisplayName') IS NOT NULL
BEGIN
    SET @sql = @sql + N'
   OR DisplayName IN (@DisplayName, @AlternateDisplayName)';
END;
ELSE IF COL_LENGTH(N'openalex.Faculty', N'FacultyDisplayName') IS NOT NULL
BEGIN
    SET @sql = @sql + N'
   OR FacultyDisplayName IN (@DisplayName, @AlternateDisplayName)';
END;
ELSE IF COL_LENGTH(N'openalex.Faculty', N'FullName') IS NOT NULL
BEGIN
    SET @sql = @sql + N'
   OR FullName IN (@DisplayName, @AlternateDisplayName)';
END;
ELSE IF COL_LENGTH(N'openalex.Faculty', N'Name') IS NOT NULL
BEGIN
    SET @sql = @sql + N'
   OR Name IN (@DisplayName, @AlternateDisplayName)';
END;

IF COL_LENGTH(N'openalex.Faculty', N'FirstName') IS NOT NULL
   AND COL_LENGTH(N'openalex.Faculty', N'LastName') IS NOT NULL
BEGIN
    SET @sql = @sql + N'
   OR (LastName = @LastName AND FirstName IN (@FirstName, N''Jeff''))';
END;

SET @sql = @sql + N'
ORDER BY FacultyId;';

SET @params = N'@DisplayName NVARCHAR(512), @AlternateDisplayName NVARCHAR(512), @FirstName NVARCHAR(200), @LastName NVARCHAR(200), @FacultyIdOut BIGINT OUTPUT';
EXEC sp_executesql
    @sql,
    @params,
    @DisplayName = @DisplayName,
    @AlternateDisplayName = @AlternateDisplayName,
    @FirstName = @FirstName,
    @LastName = @LastName,
    @FacultyIdOut = @FacultyId OUTPUT;

/*
Insert faculty row only if missing.
The insert is column-aware so it can handle minor schema differences.
*/
IF @FacultyId IS NULL
BEGIN
    DECLARE @InsertColumns NVARCHAR(MAX) = N'';
    DECLARE @InsertValues NVARCHAR(MAX) = N'';

    IF COL_LENGTH(N'openalex.Faculty', N'DisplayName') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', DisplayName';
        SET @InsertValues = @InsertValues + N', @DisplayName';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'FacultyDisplayName') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', FacultyDisplayName';
        SET @InsertValues = @InsertValues + N', @DisplayName';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'FullName') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', FullName';
        SET @InsertValues = @InsertValues + N', @DisplayName';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'Name') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', Name';
        SET @InsertValues = @InsertValues + N', @DisplayName';
    END;
    ELSE
    BEGIN
        RAISERROR('openalex.Faculty does not have a recognizable display-name column.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'FirstName') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', FirstName';
        SET @InsertValues = @InsertValues + N', @FirstName';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'LastName') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', LastName';
        SET @InsertValues = @InsertValues + N', @LastName';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'PrimaryEmail') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', PrimaryEmail';
        SET @InsertValues = @InsertValues + N', @PrimaryEmail';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'Email') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', Email';
        SET @InsertValues = @InsertValues + N', @PrimaryEmail';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'DepartmentName') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', DepartmentName';
        SET @InsertValues = @InsertValues + N', @DepartmentName';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'Department') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', Department';
        SET @InsertValues = @InsertValues + N', @DepartmentName';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'AppointmentTitle') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', AppointmentTitle';
        SET @InsertValues = @InsertValues + N', @AppointmentTitle';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'Title') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', Title';
        SET @InsertValues = @InsertValues + N', @AppointmentTitle';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'Orcid') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', Orcid';
        SET @InsertValues = @InsertValues + N', @Orcid';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'ORCID') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', ORCID';
        SET @InsertValues = @InsertValues + N', @Orcid';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'ClaimedOpenAlexAuthorId') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', ClaimedOpenAlexAuthorId';
        SET @InsertValues = @InsertValues + N', @ClaimedOpenAlexAuthorId';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'OpenAlexAuthorId') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', OpenAlexAuthorId';
        SET @InsertValues = @InsertValues + N', @ClaimedOpenAlexAuthorId';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'AuthorOpenAlexId') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', AuthorOpenAlexId';
        SET @InsertValues = @InsertValues + N', @ClaimedOpenAlexAuthorId';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'IsActive') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', IsActive';
        SET @InsertValues = @InsertValues + N', 1';
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'Active') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', Active';
        SET @InsertValues = @InsertValues + N', 1';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'SourceSystem') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', SourceSystem';
        SET @InsertValues = @InsertValues + N', N''ManualJeffMartensPilotDemo''';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'SourceFacultyKey') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', SourceFacultyKey';
        SET @InsertValues = @InsertValues + N', N''manual-jeffrey-martens''';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'CreatedAtUtc') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', CreatedAtUtc';
        SET @InsertValues = @InsertValues + N', SYSUTCDATETIME()';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'UpdatedAtUtc') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', UpdatedAtUtc';
        SET @InsertValues = @InsertValues + N', SYSUTCDATETIME()';
    END;

    IF COL_LENGTH(N'openalex.Faculty', N'LastSeenAtUtc') IS NOT NULL
    BEGIN
        SET @InsertColumns = @InsertColumns + N', LastSeenAtUtc';
        SET @InsertValues = @InsertValues + N', SYSUTCDATETIME()';
    END;

    SET @InsertColumns = STUFF(@InsertColumns, 1, 2, N'');
    SET @InsertValues = STUFF(@InsertValues, 1, 2, N'');

    SET @sql = N'
DECLARE @Inserted TABLE (FacultyId BIGINT);
INSERT INTO openalex.Faculty (' + @InsertColumns + N')
OUTPUT INSERTED.FacultyId INTO @Inserted
VALUES (' + @InsertValues + N');
SELECT @FacultyIdOut = FacultyId FROM @Inserted;';

    SET @params = N'@DisplayName NVARCHAR(512), @FirstName NVARCHAR(200), @LastName NVARCHAR(200), @PrimaryEmail NVARCHAR(320), @DepartmentName NVARCHAR(512), @AppointmentTitle NVARCHAR(512), @Orcid NVARCHAR(256), @ClaimedOpenAlexAuthorId NVARCHAR(128), @FacultyIdOut BIGINT OUTPUT';
    EXEC sp_executesql
        @sql,
        @params,
        @DisplayName = @DisplayName,
        @FirstName = @FirstName,
        @LastName = @LastName,
        @PrimaryEmail = @PrimaryEmail,
        @DepartmentName = @DepartmentName,
        @AppointmentTitle = @AppointmentTitle,
        @Orcid = @Orcid,
        @ClaimedOpenAlexAuthorId = @ClaimedOpenAlexAuthorId,
        @FacultyIdOut = @FacultyId OUTPUT;
END;
ELSE
BEGIN
    -- Make sure an existing Jeff row is active when the schema supports it.
    IF COL_LENGTH(N'openalex.Faculty', N'IsActive') IS NOT NULL
    BEGIN
        UPDATE openalex.Faculty
        SET IsActive = 1
        WHERE FacultyId = @FacultyId;
    END;
    ELSE IF COL_LENGTH(N'openalex.Faculty', N'Active') IS NOT NULL
    BEGIN
        UPDATE openalex.Faculty
        SET Active = 1
        WHERE FacultyId = @FacultyId;
    END;
END;

IF @FacultyId IS NULL
BEGIN
    RAISERROR('Failed to resolve or insert Jeff Martens faculty row.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

/*
Create/copy the new 16-person cohort.
*/
INSERT INTO openalex.PilotFacultyCohort
(
    CohortName,
    FacultyId,
    PilotOrder,
    IsEnabled
)
SELECT
    @NewCohortName,
    c.FacultyId,
    c.PilotOrder,
    c.IsEnabled
FROM openalex.PilotFacultyCohort c
WHERE c.CohortName = @ExistingCohortName
  AND NOT EXISTS
  (
      SELECT 1
      FROM openalex.PilotFacultyCohort existing
      WHERE existing.CohortName = @NewCohortName
        AND existing.FacultyId = c.FacultyId
  );

-- Add or update Jeff as the 16th enabled member of the new cohort.
IF EXISTS
(
    SELECT 1
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @NewCohortName
      AND FacultyId = @FacultyId
)
BEGIN
    UPDATE openalex.PilotFacultyCohort
    SET PilotOrder = @PilotOrder,
        IsEnabled = 1
    WHERE CohortName = @NewCohortName
      AND FacultyId = @FacultyId;
END;
ELSE
BEGIN
    INSERT INTO openalex.PilotFacultyCohort
    (
        CohortName,
        FacultyId,
        PilotOrder,
        IsEnabled
    )
    VALUES
    (
        @NewCohortName,
        @FacultyId,
        @PilotOrder,
        1
    );
END;

COMMIT TRANSACTION;

PRINT 'Jeff Martens faculty/cohort setup completed.';

SELECT
    @FacultyId AS JeffMartensFacultyId,
    @DisplayName AS JeffMartensDisplayName,
    @NewCohortName AS NewCohortName,
    @PilotOrder AS PilotOrder;

SELECT
    c.CohortName,
    COUNT(*) AS CohortRows,
    SUM(CASE WHEN c.IsEnabled = 1 THEN 1 ELSE 0 END) AS EnabledRows
FROM openalex.PilotFacultyCohort c
WHERE c.CohortName IN (@ExistingCohortName, @NewCohortName)
GROUP BY c.CohortName
ORDER BY c.CohortName;

SELECT
    c.CohortName,
    c.PilotOrder,
    c.FacultyId,
    f.DisplayName,
    f.FirstName,
    f.LastName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    f.IsActive,
    c.IsEnabled
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
WHERE c.CohortName = @NewCohortName
ORDER BY c.PilotOrder, f.DisplayName;
