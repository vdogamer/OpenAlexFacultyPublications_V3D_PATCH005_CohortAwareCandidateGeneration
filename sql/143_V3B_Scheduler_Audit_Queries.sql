/* OpenAlex Version 3B — Scheduler Audit Queries */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
IF OBJECT_ID(N'openalex.JobExecutionRequest',N'U') IS NOT NULL
BEGIN
    SELECT TOP 100 * FROM openalex.JobExecutionRequest ORDER BY JobExecutionRequestId DESC;
END
IF OBJECT_ID(N'openalex.JobExecutionEvent',N'U') IS NOT NULL
BEGIN
    SELECT TOP 200 e.*, r.JobName, r.RequestStatus
    FROM openalex.JobExecutionEvent e
    JOIN openalex.JobExecutionRequest r ON e.JobExecutionRequestId = r.JobExecutionRequestId
    ORDER BY e.JobExecutionEventId DESC;
END
IF OBJECT_ID(N'openalex.OperationalLog',N'U') IS NOT NULL
BEGIN
    SELECT TOP 100 * FROM openalex.OperationalLog WHERE Category IN ('Scheduler','QueuedJobProcessor','Worker','Pipeline') ORDER BY OperationalLogId DESC;
END
