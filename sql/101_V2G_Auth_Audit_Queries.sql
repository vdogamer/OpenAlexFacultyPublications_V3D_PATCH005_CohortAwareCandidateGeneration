/*
OpenAlex Version 2G PATCH002 — Auth Audit Queries

This version avoids assuming optional AppUser columns such as:
- ComputingId
- LastLoginAtUtc

It also avoids assuming optional AppRole metadata columns.
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Latest authentication events';

IF OBJECT_ID(N'openalex.AuthenticationEvent', N'U') IS NOT NULL
BEGIN
    SELECT TOP 100 *
    FROM openalex.AuthenticationEvent
    ORDER BY AuthenticationEventId DESC;
END
ELSE
BEGIN
    PRINT 'openalex.AuthenticationEvent does not exist.';
END

PRINT 'Users';

IF OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'openalex.AppUser', N'UserName') IS NOT NULL
    BEGIN
        SELECT *
        FROM openalex.AppUser
        ORDER BY UserName;
    END
    ELSE
    BEGIN
        SELECT *
        FROM openalex.AppUser;
    END
END
ELSE
BEGIN
    PRINT 'openalex.AppUser does not exist.';
END

PRINT 'Users and roles';

IF OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AppUserRole', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AppRole', N'U') IS NOT NULL
BEGIN
    SELECT
        u.AppUserId,
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
    PRINT 'User/role join skipped because one or more auth tables are missing.';
END

PRINT 'Recent author review decisions by reviewer';

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50
        'AuthorDecision' AS DecisionType,
        ReviewedBy,
        DecisionStatus,
        DecisionReason,
        ReviewedAtUtc,
        CreatedAtUtc
    FROM openalex.FacultyAuthorMatchDecision
    ORDER BY CreatedAtUtc DESC;
END

PRINT 'Recent publication review decisions by reviewer';

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50
        'PublicationDecision' AS DecisionType,
        ReviewedBy,
        DecisionStatus,
        DecisionReason,
        ReviewedAtUtc,
        CreatedAtUtc
    FROM openalex.FacultyPublicationDecision
    ORDER BY CreatedAtUtc DESC;
END
