/* OpenAlex Version 3C — Role Management Audit Queries */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
PRINT 'Recent role changes';
IF OBJECT_ID(N'openalex.AppRoleChangeAudit',N'U') IS NOT NULL SELECT TOP 100 * FROM openalex.AppRoleChangeAudit ORDER BY AppRoleChangeAuditId DESC;
PRINT 'External group mappings';
IF OBJECT_ID(N'openalex.AppExternalGroupRoleMapping',N'U') IS NOT NULL SELECT * FROM openalex.AppExternalGroupRoleMapping ORDER BY RoleName,ExternalGroupValue;
PRINT 'Authentication events';
IF OBJECT_ID(N'openalex.AuthenticationEvent',N'U') IS NOT NULL SELECT TOP 100 * FROM openalex.AuthenticationEvent ORDER BY AuthenticationEventId DESC;
