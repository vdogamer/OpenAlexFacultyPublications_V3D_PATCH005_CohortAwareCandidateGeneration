/*
OpenAlex Version 2H — Deployment Readiness Check
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

SELECT
    ObjectName,
    ObjectType,
    ExistsFlag =
        CASE WHEN OBJECT_ID(ObjectName, ObjectType) IS NULL THEN 0 ELSE 1 END
FROM
(
    VALUES
        (N'openalex.Institution', N'U'),
        (N'openalex.Author', N'U'),
        (N'openalex.Work', N'U'),
        (N'openalex.Faculty', N'U'),
        (N'openalex.FacultyAuthorMatchCandidate', N'U'),
        (N'openalex.FacultyAuthorMatchDecision', N'U'),
        (N'openalex.FacultyPublicationCandidate', N'U'),
        (N'openalex.FacultyPublicationDecision', N'U'),
        (N'openalex.IngestionRun', N'U'),
        (N'openalex.IngestionRunSummary', N'U'),
        (N'openalex.RequestLog', N'U'),
        (N'openalex.OperationalLog', N'U'),
        (N'openalex.AppUser', N'U'),
        (N'openalex.AppRole', N'U'),
        (N'openalex.AppUserRole', N'U'),
        (N'openalex.AuthenticationEvent', N'U'),
        (N'openalex.vw_FacultyPublicationExport', N'V'),
        (N'openalex.vw_FacultyPublicationReviewQueue', N'V')
) required(ObjectName, ObjectType)
ORDER BY ObjectName;

PRINT 'Core row counts';

DECLARE @Counts TABLE
(
    EntityName NVARCHAR(256),
    TotalRows BIGINT
);

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Faculty', COUNT_BIG(*) FROM openalex.Faculty;

IF OBJECT_ID(N'openalex.Institution', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Institution', COUNT_BIG(*) FROM openalex.Institution;

IF OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Author', COUNT_BIG(*) FROM openalex.Author;

IF OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Work', COUNT_BIG(*) FROM openalex.Work;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyAuthorMatchCandidate', COUNT_BIG(*) FROM openalex.FacultyAuthorMatchCandidate;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyAuthorMatchDecision', COUNT_BIG(*) FROM openalex.FacultyAuthorMatchDecision;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyPublicationCandidate', COUNT_BIG(*) FROM openalex.FacultyPublicationCandidate;

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyPublicationDecision', COUNT_BIG(*) FROM openalex.FacultyPublicationDecision;

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'OperationalLog', COUNT_BIG(*) FROM openalex.OperationalLog;

IF OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'AppUser', COUNT_BIG(*) FROM openalex.AppUser;

SELECT EntityName, TotalRows
FROM @Counts
ORDER BY EntityName;
