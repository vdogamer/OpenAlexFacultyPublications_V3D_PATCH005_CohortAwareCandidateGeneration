/*
OpenAlex Version 2H — Job Run Audit Queries
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Recent ingestion and pipeline runs';

IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
BEGIN
    SELECT TOP 100
        IngestionRunId,
        RunMode,
        Status,
        InstitutionFilter,
        StartedAtUtc,
        FinishedAtUtc,
        ElapsedMs,
        StartedBy,
        MachineName,
        ApplicationVersion,
        ErrorCount,
        WarningCount
    FROM openalex.IngestionRun
    ORDER BY IngestionRunId DESC;
END

PRINT 'Recent run summaries';

IF OBJECT_ID(N'openalex.IngestionRunSummary', N'U') IS NOT NULL
BEGIN
    SELECT TOP 100 *
    FROM openalex.IngestionRunSummary
    ORDER BY IngestionRunSummaryId DESC;
END

PRINT 'Recent request logs';

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 100
        RequestLogId,
        IngestionRunId,
        EntityType,
        RequestMethod,
        RequestUrlMasked,
        PageNumberWithinRun,
        HttpStatusCode,
        RequestSucceeded,
        StartedAtUtc,
        FinishedAtUtc,
        ElapsedMs,
        ResultCount,
        ErrorMessage
    FROM openalex.RequestLog
    ORDER BY RequestLogId DESC;
END

PRINT 'Recent pipeline/worker operational logs';

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 100
        OperationalLogId,
        SourceApplication,
        EnvironmentName,
        LogLevel,
        Category,
        EventName,
        Message,
        CreatedAtUtc
    FROM openalex.OperationalLog
    WHERE Category IN
    (
        'Pipeline',
        'Worker',
        'InstitutionIngestion',
        'AuthorIngestion',
        'WorkIngestion',
        'FacultyAuthorCandidateGeneration',
        'FacultyPublicationCandidateGeneration'
    )
    ORDER BY OperationalLogId DESC;
END
