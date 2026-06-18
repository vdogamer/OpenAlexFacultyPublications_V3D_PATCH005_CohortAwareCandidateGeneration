/*
OpenAlex Version 2C — Institution Ingestion Demo Queries
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Latest ingestion runs';

SELECT TOP 20
    IngestionRunId,
    RunCorrelationId,
    RunMode,
    Status,
    InstitutionFilter,
    StartedAtUtc,
    FinishedAtUtc,
    ElapsedMs,
    MaxPagesPerEntity,
    MaxRecordsPerEntity,
    StartedBy,
    MachineName,
    ApplicationVersion,
    ErrorCount,
    WarningCount
FROM openalex.IngestionRun
ORDER BY IngestionRunId DESC;

PRINT 'Latest institution request logs';

SELECT TOP 20
    RequestLogId,
    IngestionRunId,
    EntityType,
    RequestMethod,
    RequestUrlMasked,
    FilterText,
    CursorValue,
    PageNumberWithinRun,
    HttpStatusCode,
    RequestSucceeded,
    StartedAtUtc,
    FinishedAtUtc,
    ElapsedMs,
    MetaCount,
    MetaDbResponseTimeMs,
    MetaNextCursor,
    ResultCount,
    ErrorMessage
FROM openalex.RequestLog
WHERE EntityType = 'Institution'
ORDER BY RequestLogId DESC;

PRINT 'Latest institution rows';

SELECT TOP 20
    InstitutionId,
    OpenAlexId,
    OpenAlexShortId,
    Ror,
    DisplayName,
    CountryCode,
    InstitutionType,
    WorksCount,
    CitedByCount,
    LastIngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc,
    LastSeenAtUtc,
    LEN(RawJson) AS RawJsonLength,
    RawJsonHash
FROM openalex.Institution
ORDER BY LastSeenAtUtc DESC, InstitutionId DESC;

PRINT 'Institution IDs';

SELECT TOP 20
    ids.InstitutionIdsId,
    ids.InstitutionId,
    i.DisplayName,
    ids.InstitutionOpenAlexId,
    ids.OpenAlex,
    ids.Ror,
    ids.Wikidata,
    ids.Wikipedia,
    ids.Mag,
    ids.Grid
FROM openalex.InstitutionIds ids
JOIN openalex.Institution i
    ON ids.InstitutionId = i.InstitutionId
ORDER BY ids.InstitutionIdsId DESC;

PRINT 'Latest summaries';

SELECT TOP 20
    IngestionRunSummaryId,
    IngestionRunId,
    TotalRequests,
    SuccessfulRequests,
    FailedRequests,
    InstitutionsProcessed,
    InstitutionsInserted,
    InstitutionsUpdated,
    SummaryText,
    CreatedAtUtc
FROM openalex.IngestionRunSummary
ORDER BY IngestionRunSummaryId DESC;

PRINT 'Operational log evidence';

SELECT TOP 50
    OperationalLogId,
    SourceApplication,
    EnvironmentName,
    LogLevel,
    Category,
    EventName,
    Message,
    CreatedAtUtc
FROM openalex.OperationalLog
WHERE Category IN ('InstitutionIngestion', 'Worker')
ORDER BY OperationalLogId DESC;
