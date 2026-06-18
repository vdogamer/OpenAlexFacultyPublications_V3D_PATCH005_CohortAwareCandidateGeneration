/* OpenAlex Version 3D — Execution Window Readiness Check */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
SELECT ObjectName,ObjectType,ExistsFlag=CASE WHEN OBJECT_ID(ObjectName,ObjectType) IS NULL THEN 0 ELSE 1 END
FROM (VALUES
(N'openalex.JobExecutionWindow',N'U'),(N'openalex.JobDefinition',N'U'),(N'openalex.JobExecutionRequest',N'U'),(N'openalex.JobExecutionEvent',N'U'),(N'openalex.OperationalLog',N'U')
) required(ObjectName,ObjectType) ORDER BY ObjectName;
PRINT 'Enabled execution windows';
IF OBJECT_ID(N'openalex.JobExecutionWindow',N'U') IS NOT NULL
BEGIN
    SELECT JobExecutionWindowId,WindowName,AppliesToJobName,IsEnabled,DaysOfWeekCsv,StartTimeLocal,EndTimeLocal,TimeZoneId,AllowFullLoad,MaxRequestsPerWindow,UpdatedAtUtc
    FROM openalex.JobExecutionWindow ORDER BY IsEnabled DESC, WindowName;
END
PRINT 'Queued job requests';
IF OBJECT_ID(N'openalex.JobExecutionRequest',N'U') IS NOT NULL
BEGIN
    SELECT TOP 50 JobExecutionRequestId,JobName,RequestStatus,RequestedBy,RequestedAtUtc,RunMode,MaxPages,MaxRecords,StartedAtUtc,FinishedAtUtc,ErrorMessage
    FROM openalex.JobExecutionRequest ORDER BY JobExecutionRequestId DESC;
END
