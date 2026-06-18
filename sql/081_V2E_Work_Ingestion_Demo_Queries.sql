/*
OpenAlex Version 2E — Work/Publication Ingestion Demo Queries
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

PRINT 'Latest work request logs';

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
WHERE EntityType = 'Work'
ORDER BY RequestLogId DESC;

PRINT 'Latest work rows';

SELECT TOP 50
    WorkId,
    OpenAlexId,
    OpenAlexShortId,
    Doi,
    LEFT(COALESCE(Title, DisplayName), 200) AS WorkTitle,
    PublicationYear,
    PublicationDate,
    WorkType,
    Language,
    CitedByCount,
    AuthorsCount,
    InstitutionsDistinctCount,
    LastIngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc,
    LastSeenAtUtc,
    LEN(RawJson) AS RawJsonLength,
    RawJsonHash
FROM openalex.Work
ORDER BY LastSeenAtUtc DESC, WorkId DESC;

PRINT 'Latest work authorships';

SELECT TOP 50
    wa.WorkAuthorshipId,
    wa.WorkId,
    LEFT(w.Title, 160) AS WorkTitle,
    wa.AuthorOpenAlexId,
    wa.AuthorDisplayName,
    wa.AuthorOrcid,
    wa.IsCorresponding,
    wa.OrdinalPosition,
    wa.CreatedAtUtc
FROM openalex.WorkAuthorship wa
JOIN openalex.Work w
    ON wa.WorkId = w.WorkId
ORDER BY wa.WorkAuthorshipId DESC;

PRINT 'Latest authorship institutions';

SELECT TOP 50
    wai.WorkAuthorshipInstitutionId,
    wai.WorkId,
    LEFT(w.Title, 160) AS WorkTitle,
    wai.InstitutionOpenAlexId,
    wai.InstitutionDisplayName,
    wai.InstitutionRor,
    wai.CountryCode,
    wai.InstitutionType,
    wai.OrdinalPosition,
    wai.CreatedAtUtc
FROM openalex.WorkAuthorshipInstitution wai
JOIN openalex.Work w
    ON wai.WorkId = w.WorkId
ORDER BY wai.WorkAuthorshipInstitutionId DESC;

PRINT 'Matched configured institutions';

SELECT TOP 50
    mi.WorkMatchedInstitutionId,
    mi.WorkId,
    LEFT(w.Title, 160) AS WorkTitle,
    mi.InstitutionOpenAlexId,
    mi.MatchedFromField,
    mi.MatchSource,
    mi.IngestionRunId,
    mi.CreatedAtUtc
FROM openalex.WorkMatchedInstitution mi
JOIN openalex.Work w
    ON mi.WorkId = w.WorkId
ORDER BY mi.WorkMatchedInstitutionId DESC;

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
    WorksProcessed,
    WorksInserted,
    WorksUpdated,
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
WHERE Category IN ('WorkIngestion', 'Worker')
ORDER BY OperationalLogId DESC;
