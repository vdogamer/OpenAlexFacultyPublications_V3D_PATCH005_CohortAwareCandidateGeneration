/*
OpenAlex Version 2B — Migrated Core Logic Readiness Check
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
        (N'openalex.vw_FacultyPublicationReviewQueue', N'V'),
        (N'openalex.vw_FacultyPublicationExport', N'V'),
        (N'openalex.OperationalLog', N'U')
) required(ObjectName, ObjectType)
ORDER BY ObjectName;

DECLARE @Counts TABLE
(
    EntityName NVARCHAR(256),
    TotalRows BIGINT
);

IF OBJECT_ID(N'openalex.Institution', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Institution', COUNT_BIG(*) FROM openalex.Institution;

IF OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Author', COUNT_BIG(*) FROM openalex.Author;

IF OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Work', COUNT_BIG(*) FROM openalex.Work;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'Faculty', COUNT_BIG(*) FROM openalex.Faculty;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyAuthorMatchCandidate', COUNT_BIG(*) FROM openalex.FacultyAuthorMatchCandidate;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyPublicationCandidate', COUNT_BIG(*) FROM openalex.FacultyPublicationCandidate;

IF OBJECT_ID(N'openalex.vw_FacultyPublicationExport', N'V') IS NOT NULL
    INSERT INTO @Counts SELECT N'FacultyPublicationExportView', COUNT_BIG(*) FROM openalex.vw_FacultyPublicationExport;

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
    INSERT INTO @Counts SELECT N'OperationalLog', COUNT_BIG(*) FROM openalex.OperationalLog;

SELECT
    EntityName,
    TotalRows
FROM @Counts
ORDER BY EntityName;
