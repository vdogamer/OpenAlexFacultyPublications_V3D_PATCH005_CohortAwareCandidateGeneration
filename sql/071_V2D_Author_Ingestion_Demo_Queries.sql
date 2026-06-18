/*
OpenAlex Version 2D — Author Ingestion Demo Queries
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

PRINT 'Latest author request logs';

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
WHERE EntityType = 'Author'
ORDER BY RequestLogId DESC;

PRINT 'Latest author rows';

SELECT TOP 50
    AuthorId,
    OpenAlexId,
    OpenAlexShortId,
    Orcid,
    DisplayName,
    WorksCount,
    CitedByCount,
    SummaryStatsHIndex,
    SummaryStatsI10Index,
    LastIngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc,
    LastSeenAtUtc,
    LEN(RawJson) AS RawJsonLength,
    RawJsonHash
FROM openalex.Author
ORDER BY LastSeenAtUtc DESC, AuthorId DESC;

PRINT 'Author last-known institutions';

SELECT TOP 50
    lki.AuthorLastKnownInstitutionId,
    a.DisplayName AS AuthorDisplayName,
    lki.AuthorOpenAlexId,
    lki.InstitutionOpenAlexId,
    lki.InstitutionDisplayName,
    lki.InstitutionRor,
    lki.CountryCode,
    lki.InstitutionType,
    lki.OrdinalPosition,
    lki.CreatedAtUtc
FROM openalex.AuthorLastKnownInstitution lki
JOIN openalex.Author a
    ON lki.AuthorId = a.AuthorId
ORDER BY lki.AuthorLastKnownInstitutionId DESC;

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
    AuthorsProcessed,
    AuthorsInserted,
    AuthorsUpdated,
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
WHERE Category IN ('AuthorIngestion', 'Worker')
ORDER BY OperationalLogId DESC;
