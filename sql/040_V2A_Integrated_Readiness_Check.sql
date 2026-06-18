/*
OpenAlex Version 2A — Integrated Application Readiness Check
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Required V1/V2 database objects';

SELECT
    ObjectName,
    ObjectType,
    ExistsFlag =
        CASE
            WHEN OBJECT_ID(ObjectName, ObjectType) IS NULL THEN 0
            ELSE 1
        END
FROM
(
    VALUES
        (N'openalex.IngestionRun', N'U'),
        (N'openalex.RequestLog', N'U'),
        (N'openalex.Institution', N'U'),
        (N'openalex.Author', N'U'),
        (N'openalex.Work', N'U'),
        (N'openalex.Faculty', N'U'),
        (N'openalex.FacultyAuthorMatchCandidate', N'U'),
        (N'openalex.FacultyAuthorMatchDecision', N'U'),
        (N'openalex.FacultyPublicationCandidate', N'U'),
        (N'openalex.FacultyPublicationDecision', N'U'),
        (N'openalex.vw_FacultyAcceptedAuthors', N'V'),
        (N'openalex.vw_FacultyPublicationReviewQueue', N'V'),
        (N'openalex.vw_FacultyAcceptedPublications', N'V'),
        (N'openalex.vw_FacultyPublicationExport', N'V'),
        (N'openalex.vw_FacultyMatchAuditTrail', N'V'),
        (N'openalex.BackgroundJobDefinition', N'U'),
        (N'openalex.AppUser', N'U'),
        (N'openalex.AppRole', N'U'),
        (N'openalex.OperationalLog', N'U'),
        (N'openalex.ApplicationError', N'U'),
        (N'openalex.HealthCheckResult', N'U')
) v(ObjectName, ObjectType)
ORDER BY ObjectName;

PRINT 'Core row counts';

DECLARE @Counts TABLE
(
    EntityName NVARCHAR(256),
    TotalRows BIGINT
);

IF OBJECT_ID(N'openalex.Institution', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Institution', COUNT(*) FROM openalex.Institution;

IF OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Author', COUNT(*) FROM openalex.Author;

IF OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Work', COUNT(*) FROM openalex.Work;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Faculty', COUNT(*) FROM openalex.Faculty;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyAuthorMatchCandidate', COUNT(*) FROM openalex.FacultyAuthorMatchCandidate;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyPublicationCandidate', COUNT(*) FROM openalex.FacultyPublicationCandidate;

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'OperationalLog', COUNT(*) FROM openalex.OperationalLog;

SELECT EntityName, TotalRows
FROM @Counts
ORDER BY EntityName;
