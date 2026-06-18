/*
OpenAlex Version 2F — Candidate Generation + Review Readiness Check
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
        (N'openalex.IngestionRun', N'U'),
        (N'openalex.IngestionRunSummary', N'U'),
        (N'openalex.Faculty', N'U'),
        (N'openalex.Author', N'U'),
        (N'openalex.AuthorLastKnownInstitution', N'U'),
        (N'openalex.Work', N'U'),
        (N'openalex.WorkAuthorship', N'U'),
        (N'openalex.WorkAuthorshipInstitution', N'U'),
        (N'openalex.FacultyAuthorMatchCandidate', N'U'),
        (N'openalex.FacultyAuthorMatchDecision', N'U'),
        (N'openalex.FacultyPublicationCandidate', N'U'),
        (N'openalex.FacultyPublicationDecision', N'U'),
        (N'openalex.vw_FacultyPublicationReviewQueue', N'V'),
        (N'openalex.vw_FacultyPublicationExport', N'V'),
        (N'openalex.OperationalLog', N'U')
) required(ObjectName, ObjectType)
ORDER BY ObjectName;

SELECT
    EntityName,
    TotalRows
FROM
(
    SELECT 'Faculty' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.Faculty
    UNION ALL
    SELECT 'Author' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.Author
    UNION ALL
    SELECT 'AuthorLastKnownInstitution' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.AuthorLastKnownInstitution
    UNION ALL
    SELECT 'Work' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.Work
    UNION ALL
    SELECT 'WorkAuthorship' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.WorkAuthorship
    UNION ALL
    SELECT 'FacultyAuthorMatchCandidate' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.FacultyAuthorMatchCandidate
    UNION ALL
    SELECT 'FacultyAuthorMatchDecision' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.FacultyAuthorMatchDecision
    UNION ALL
    SELECT 'FacultyPublicationCandidate' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.FacultyPublicationCandidate
    UNION ALL
    SELECT 'FacultyPublicationDecision' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.FacultyPublicationDecision
) counts
ORDER BY EntityName;
