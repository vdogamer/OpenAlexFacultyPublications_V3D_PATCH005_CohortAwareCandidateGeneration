/* OpenAlex Version 3B — Scheduler Readiness Check */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
SELECT ObjectName, ObjectType, ExistsFlag = CASE WHEN OBJECT_ID(ObjectName, ObjectType) IS NULL THEN 0 ELSE 1 END
FROM (VALUES
(N'openalex.JobDefinition',N'U'),(N'openalex.JobExecutionRequest',N'U'),(N'openalex.JobExecutionEvent',N'U'),
(N'openalex.AppUser',N'U'),(N'openalex.AppRole',N'U'),(N'openalex.AppUserRole',N'U'),(N'openalex.OperationalLog',N'U'),(N'openalex.IngestionRun',N'U')
) required(ObjectName,ObjectType)
ORDER BY ObjectName;
IF OBJECT_ID(N'openalex.JobDefinition',N'U') IS NOT NULL SELECT * FROM openalex.JobDefinition ORDER BY SortOrder, JobName;
IF OBJECT_ID(N'openalex.JobExecutionRequest',N'U') IS NOT NULL SELECT TOP 50 * FROM openalex.JobExecutionRequest ORDER BY JobExecutionRequestId DESC;
