/*
OpenAlex Version 2G PATCH001 — Auth/Role Readiness Check

This version avoids assuming optional AppRole columns such as:
- RoleDescription
- IsSystemRole

It should work with the actual V1N auth schema as long as these core objects exist:
- openalex.AppUser
- openalex.AppRole
- openalex.AppUserRole
- openalex.AuthenticationEvent
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

SELECT
    ObjectName,
    ObjectType,
    ExistsFlag =
        CASE WHEN OBJECT_ID(ObjectName, ObjectType) IS NULL THEN 0 ELSE 1 END
FROM
(
    VALUES
        (N'openalex.AppUser', N'U'),
        (N'openalex.AppRole', N'U'),
        (N'openalex.AppUserRole', N'U'),
        (N'openalex.AppExternalRoleMapping', N'U'),
        (N'openalex.AuthenticationEvent', N'U'),
        (N'openalex.vw_AppUserRoleSummary', N'V')
) required(ObjectName, ObjectType)
ORDER BY ObjectName;

PRINT 'AppRole rows';

IF OBJECT_ID(N'openalex.AppRole', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'openalex.AppRole', N'RoleName') IS NOT NULL
    BEGIN
        SELECT *
        FROM openalex.AppRole
        ORDER BY RoleName;
    END
    ELSE
    BEGIN
        SELECT *
        FROM openalex.AppRole;
    END
END
ELSE
BEGIN
    PRINT 'openalex.AppRole does not exist. Run the V1N auth schema/seed scripts first.';
END

PRINT 'User role summary';

IF OBJECT_ID(N'openalex.vw_AppUserRoleSummary', N'V') IS NOT NULL
BEGIN
    SELECT TOP 100 *
    FROM openalex.vw_AppUserRoleSummary
    ORDER BY UserName;
END
ELSE IF OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.AppUserRole', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.AppRole', N'U') IS NOT NULL
BEGIN
    SELECT
        u.UserName,
        u.DisplayName,
        u.Email,
        u.IsEnabled,
        r.RoleName
    FROM openalex.AppUser u
    LEFT JOIN openalex.AppUserRole ur
        ON u.AppUserId = ur.AppUserId
    LEFT JOIN openalex.AppRole r
        ON ur.AppRoleId = r.AppRoleId
    ORDER BY u.UserName, r.RoleName;
END
ELSE
BEGIN
    PRINT 'User/role summary could not be displayed because one or more auth tables are missing.';
END

PRINT 'Expected demo users';

SELECT ExpectedUserName, ExpectedRole
FROM
(
    VALUES
        ('manager.demo', 'Reader'),
        ('reviewer.demo', 'Reader + Reviewer'),
        ('admin.demo', 'Reader + Reviewer + Admin')
) expected(ExpectedUserName, ExpectedRole);

PRINT 'Demo user verification';

IF OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AppUserRole', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AppRole', N'U') IS NOT NULL
BEGIN
    DECLARE @Expected TABLE
    (
        UserName NVARCHAR(256),
        RoleName NVARCHAR(100)
    );

    INSERT INTO @Expected (UserName, RoleName)
    VALUES
        (N'manager.demo', N'Reader'),
        (N'reviewer.demo', N'Reader'),
        (N'reviewer.demo', N'Reviewer'),
        (N'admin.demo', N'Reader'),
        (N'admin.demo', N'Reviewer'),
        (N'admin.demo', N'Admin');

    SELECT
        e.UserName,
        e.RoleName,
        ExistsFlag =
            CASE WHEN ur.AppUserRoleId IS NULL THEN 0 ELSE 1 END
    FROM @Expected e
    LEFT JOIN openalex.AppUser u
        ON e.UserName = u.UserName
    LEFT JOIN openalex.AppRole r
        ON e.RoleName = r.RoleName
    LEFT JOIN openalex.AppUserRole ur
        ON u.AppUserId = ur.AppUserId
       AND r.AppRoleId = ur.AppRoleId
    ORDER BY e.UserName, e.RoleName;
END
ELSE
BEGIN
    PRINT 'Demo user verification skipped because one or more auth tables are missing.';
END
