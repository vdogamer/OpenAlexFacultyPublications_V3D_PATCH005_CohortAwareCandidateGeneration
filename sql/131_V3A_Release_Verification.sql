/* OpenAlex Version 3A — Release Verification */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
PRINT 'Recent runs';
IF OBJECT_ID(N'openalex.IngestionRun',N'U') IS NOT NULL SELECT TOP 30 IngestionRunId,RunMode,Status,InstitutionFilter,StartedAtUtc,FinishedAtUtc,ElapsedMs,StartedBy,ApplicationVersion,ErrorCount,WarningCount FROM openalex.IngestionRun ORDER BY IngestionRunId DESC;
PRINT 'Operational warnings/errors';
IF OBJECT_ID(N'openalex.OperationalLog',N'U') IS NOT NULL SELECT TOP 50 OperationalLogId,SourceApplication,EnvironmentName,LogLevel,Category,EventName,Message,CreatedAtUtc FROM openalex.OperationalLog WHERE LogLevel IN ('Error','Critical','Warning') ORDER BY OperationalLogId DESC;
PRINT 'Accepted publications';
IF OBJECT_ID(N'openalex.FacultyPublicationDecision',N'U') IS NOT NULL SELECT TOP 50 FacultyPublicationDecisionId,FacultyId,WorkId,WorkTitle,PublicationYear,WorkDoi,DecisionStatus,IsCurrent,ReviewedBy,ReviewedAtUtc FROM openalex.FacultyPublicationDecision WHERE DecisionStatus='Accepted' AND IsCurrent=1 ORDER BY ReviewedAtUtc DESC, CreatedAtUtc DESC;
PRINT 'Users and roles';
IF OBJECT_ID(N'openalex.AppUser',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AppUserRole',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AppRole',N'U') IS NOT NULL SELECT u.UserName,u.DisplayName,u.Email,u.IsEnabled,r.RoleName FROM openalex.AppUser u LEFT JOIN openalex.AppUserRole ur ON u.AppUserId=ur.AppUserId LEFT JOIN openalex.AppRole r ON ur.AppRoleId=r.AppRoleId ORDER BY u.UserName,r.RoleName;
