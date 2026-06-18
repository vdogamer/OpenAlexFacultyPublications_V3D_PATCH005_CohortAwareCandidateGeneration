/*
Controlled OpenAlex TestLoad — After Action Report
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Recent ingestion runs';

IF OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
BEGIN
    SELECT TOP 20
        IngestionRunId,
        RunMode,
        Status,
        InstitutionFilter,
        StartedAtUtc,
        FinishedAtUtc,
        ElapsedMs,
        StartedBy,
        ApplicationVersion,
        ErrorCount,
        WarningCount
    FROM openalex.IngestionRun
    ORDER BY IngestionRunId DESC;
END

PRINT 'Recent request logs';

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 50
        RequestLogId,
        IngestionRunId,
        EntityType,
        RequestMethod,
        RequestUrlMasked,
        HttpStatusCode,
        RequestSucceeded,
        ResultCount,
        ErrorMessage,
        StartedAtUtc,
        FinishedAtUtc
    FROM openalex.RequestLog
    ORDER BY RequestLogId DESC;
END

PRINT 'Core row counts';

DECLARE @Counts TABLE
(
    EntityName NVARCHAR(200),
    TotalRows BIGINT
);

IF OBJECT_ID(N'openalex.Institution', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'Institution', COUNT_BIG(*) FROM openalex.Institution;

IF OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'Author', COUNT_BIG(*) FROM openalex.Author;

IF OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'Work', COUNT_BIG(*) FROM openalex.Work;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'FacultyAuthorMatchCandidate', COUNT_BIG(*) FROM openalex.FacultyAuthorMatchCandidate;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'FacultyPublicationCandidate', COUNT_BIG(*) FROM openalex.FacultyPublicationCandidate;

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT 'AcceptedPublicationDecision', COUNT_BIG(*) FROM openalex.FacultyPublicationDecision WHERE DecisionStatus = 'Accepted' AND IsCurrent = 1;

SELECT *
FROM @Counts
ORDER BY EntityName;
