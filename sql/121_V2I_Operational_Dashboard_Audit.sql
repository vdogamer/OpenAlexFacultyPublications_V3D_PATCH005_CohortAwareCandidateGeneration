/*
OpenAlex Version 2I — Operational Dashboard Audit
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Operational metric counts';

DECLARE @Counts TABLE
(
    MetricName NVARCHAR(200),
    MetricValue BIGINT
);

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'OperationalLog rows', COUNT_BIG(*) FROM openalex.OperationalLog;

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'OperationalLog error/critical rows', COUNT_BIG(*) FROM openalex.OperationalLog WHERE LogLevel IN ('Error', 'Critical');

IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'IngestionRun rows', COUNT_BIG(*) FROM openalex.IngestionRun;

IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'Failed/warning ingestion runs', COUNT_BIG(*) FROM openalex.IngestionRun WHERE Status IN ('Failed', 'CompletedWithWarnings');

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'RequestLog rows', COUNT_BIG(*) FROM openalex.RequestLog;

IF OBJECT_ID(N'openalex.ApplicationError', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'ApplicationError rows', COUNT_BIG(*) FROM openalex.ApplicationError;

IF OBJECT_ID(N'openalex.HealthCheckResult', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'HealthCheckResult rows', COUNT_BIG(*) FROM openalex.HealthCheckResult;

SELECT *
FROM @Counts
ORDER BY MetricName;

PRINT 'Recent operational logs';

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50 *
    FROM openalex.OperationalLog
    ORDER BY CreatedAtUtc DESC;
END

PRINT 'Recent ingestion runs';

IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50 *
    FROM openalex.IngestionRun
    ORDER BY IngestionRunId DESC;
END

PRINT 'Recent request logs';

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50 *
    FROM openalex.RequestLog
    ORDER BY RequestLogId DESC;
END
