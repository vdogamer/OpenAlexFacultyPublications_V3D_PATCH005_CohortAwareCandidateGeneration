/* OpenAlex Version 3D — Execution Window Audit Queries */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
PRINT 'Execution windows';
IF OBJECT_ID(N'openalex.JobExecutionWindow',N'U') IS NOT NULL SELECT * FROM openalex.JobExecutionWindow ORDER BY IsEnabled DESC, WindowName;
PRINT 'Scheduler requests';
IF OBJECT_ID(N'openalex.JobExecutionRequest',N'U') IS NOT NULL SELECT TOP 100 * FROM openalex.JobExecutionRequest ORDER BY JobExecutionRequestId DESC;
PRINT 'Scheduler events';
IF OBJECT_ID(N'openalex.JobExecutionEvent',N'U') IS NOT NULL SELECT TOP 200 * FROM openalex.JobExecutionEvent ORDER BY JobExecutionEventId DESC;
PRINT 'Worker logs';
IF OBJECT_ID(N'openalex.OperationalLog',N'U') IS NOT NULL
BEGIN
    SELECT TOP 100 OperationalLogId,SourceApplication,EnvironmentName,LogLevel,Category,EventName,Message,CreatedAtUtc
    FROM openalex.OperationalLog WHERE Category IN ('Worker','QueuedJobProcessor','ExecutionWindow','Scheduler') ORDER BY OperationalLogId DESC;
END
