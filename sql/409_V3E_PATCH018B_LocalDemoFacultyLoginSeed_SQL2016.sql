/*
V3E PATCH018B - LOCAL/DEMO ONLY Faculty Self-Service Login Seed
SQL Server 2016 compatible.

Purpose
-------
Create or refresh a DemoCookie Reader login that resolves, through the same
email/computing-id claims used in production, to the existing FacultyId 16
demo faculty record.

DO NOT use this script as a production identity-provisioning mechanism.
Production uses ShibbolethHeaders and real UVA identity claims.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @FacultyId BIGINT = 16;
DECLARE @DemoUserName NVARCHAR(256) = N'faculty.demo.farber';

IF OBJECT_ID(N'openalex.AppUser', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AppRole', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AppUserRole', N'U') IS NULL
BEGIN
    RAISERROR('Authentication tables are not installed.', 16, 1);
    RETURN;
END;

DECLARE
    @DisplayName NVARCHAR(512),
    @Email NVARCHAR(512),
    @ComputingId NVARCHAR(256);

SELECT
    @DisplayName = f.DisplayName,
    @Email = f.PrimaryEmail
FROM openalex.Faculty f
WHERE f.FacultyId = @FacultyId;

IF @DisplayName IS NULL OR @Email IS NULL
BEGIN
    RAISERROR('Demo faculty record or PrimaryEmail was not found.', 16, 1);
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyAcademicOnlineAuthorMap m
    WHERE m.FacultyId = @FacultyId
      AND m.IsCurrent = 1
)
BEGIN
    RAISERROR('Demo faculty does not have a current Academic Online mapping. Run PATCH018 mapping setup first.', 16, 1);
    RETURN;
END;

SET @ComputingId =
    CASE
        WHEN CHARINDEX('@', @Email) > 1
            THEN LEFT(@Email, CHARINDEX('@', @Email) - 1)
        ELSE @Email
    END;

DECLARE @AppUserId BIGINT;
DECLARE @ReaderRoleId BIGINT;

BEGIN TRANSACTION;

SELECT @AppUserId = AppUserId
FROM openalex.AppUser
WHERE UserName = @DemoUserName;

IF @AppUserId IS NULL
BEGIN
    IF COL_LENGTH(N'openalex.AppUser', N'ComputingId') IS NOT NULL
    BEGIN
        INSERT INTO openalex.AppUser
        (
            UserName,
            ComputingId,
            DisplayName,
            Email,
            IsEnabled,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            @DemoUserName,
            @ComputingId,
            @DisplayName,
            @Email,
            1,
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );
    END
    ELSE
    BEGIN
        INSERT INTO openalex.AppUser
        (
            UserName,
            DisplayName,
            Email,
            IsEnabled,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            @DemoUserName,
            @DisplayName,
            @Email,
            1,
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );
    END;

    SET @AppUserId = SCOPE_IDENTITY();
END
ELSE
BEGIN
    IF COL_LENGTH(N'openalex.AppUser', N'ComputingId') IS NOT NULL
    BEGIN
        UPDATE openalex.AppUser
        SET
            ComputingId = @ComputingId,
            DisplayName = @DisplayName,
            Email = @Email,
            IsEnabled = 1,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE AppUserId = @AppUserId;
    END
    ELSE
    BEGIN
        UPDATE openalex.AppUser
        SET
            DisplayName = @DisplayName,
            Email = @Email,
            IsEnabled = 1,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE AppUserId = @AppUserId;
    END;
END;

SELECT @ReaderRoleId = AppRoleId
FROM openalex.AppRole
WHERE RoleName = 'Reader';

IF @ReaderRoleId IS NULL
BEGIN
    RAISERROR('Reader role does not exist.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM openalex.AppUserRole
    WHERE AppUserId = @AppUserId
      AND AppRoleId = @ReaderRoleId
)
BEGIN
    INSERT INTO openalex.AppUserRole
    (
        AppUserId,
        AppRoleId,
        CreatedAtUtc
    )
    VALUES
    (
        @AppUserId,
        @ReaderRoleId,
        SYSUTCDATETIME()
    );
END;

COMMIT TRANSACTION;

SELECT
    DemoUserName = @DemoUserName,
    AppUserId = @AppUserId,
    FacultyId = @FacultyId,
    FacultyDisplayName = @DisplayName,
    FacultyEmail = @Email,
    ComputingId = @ComputingId,
    RoleName = 'Reader',
    DemoStatus = 'Ready_LoginWithDemoUserName_ThenOpen_Faculty_MyPublications';
