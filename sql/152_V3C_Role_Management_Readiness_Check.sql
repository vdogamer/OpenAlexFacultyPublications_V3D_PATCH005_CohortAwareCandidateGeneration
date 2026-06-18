/* OpenAlex Version 3C — Role Management Readiness Check */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
SELECT ObjectName,ObjectType,ExistsFlag=CASE WHEN OBJECT_ID(ObjectName,ObjectType) IS NULL THEN 0 ELSE 1 END
FROM (VALUES (N'openalex.AppUser',N'U'),(N'openalex.AppRole',N'U'),(N'openalex.AppUserRole',N'U'),(N'openalex.AuthenticationEvent',N'U'),(N'openalex.AppRoleChangeAudit',N'U'),(N'openalex.AppExternalGroupRoleMapping',N'U')) required(ObjectName,ObjectType)
ORDER BY ObjectName;
PRINT 'Users and roles';
IF OBJECT_ID(N'openalex.AppUser',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AppUserRole',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AppRole',N'U') IS NOT NULL
BEGIN
    SELECT u.AppUserId,u.UserName,u.DisplayName,u.Email,u.IsEnabled,r.RoleName
    FROM openalex.AppUser u
    LEFT JOIN openalex.AppUserRole ur ON u.AppUserId=ur.AppUserId
    LEFT JOIN openalex.AppRole r ON ur.AppRoleId=r.AppRoleId
    ORDER BY u.UserName,r.RoleName;
END
PRINT 'Enabled Admin count';
IF OBJECT_ID(N'openalex.AppUser',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AppUserRole',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AppRole',N'U') IS NOT NULL
BEGIN
    SELECT EnabledAdminCount=COUNT_BIG(*)
    FROM openalex.AppUser u JOIN openalex.AppUserRole ur ON u.AppUserId=ur.AppUserId JOIN openalex.AppRole r ON ur.AppRoleId=r.AppRoleId
    WHERE u.IsEnabled=1 AND r.RoleName='Admin';
END
PRINT 'External group mappings';
IF OBJECT_ID(N'openalex.AppExternalGroupRoleMapping',N'U') IS NOT NULL SELECT * FROM openalex.AppExternalGroupRoleMapping ORDER BY RoleName,ExternalGroupValue;
