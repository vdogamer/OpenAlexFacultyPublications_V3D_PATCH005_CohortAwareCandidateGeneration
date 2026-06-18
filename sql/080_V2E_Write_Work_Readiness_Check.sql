/*
OpenAlex Version 2E — Write-enabled Work/Publication Ingestion Readiness Check
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
        (N'openalex.RequestLog', N'U'),
        (N'openalex.Institution', N'U'),
        (N'openalex.Author', N'U'),
        (N'openalex.Work', N'U'),
        (N'openalex.WorkAuthorship', N'U'),
        (N'openalex.WorkAuthorshipInstitution', N'U'),
        (N'openalex.WorkMatchedInstitution', N'U'),
        (N'openalex.OperationalLog', N'U')
) required(ObjectName, ObjectType)
ORDER BY ObjectName;

PRINT 'Version 2E defaults to RunMode = TestLoad to satisfy CK_IngestionRun_RunMode.';

SELECT
    EntityName,
    TotalRows
FROM
(
    SELECT 'IngestionRun' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.IngestionRun
    UNION ALL
    SELECT 'RequestLog' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.RequestLog
    UNION ALL
    SELECT 'Institution' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.Institution
    UNION ALL
    SELECT 'Author' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.Author
    UNION ALL
    SELECT 'Work' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.Work
    UNION ALL
    SELECT 'WorkAuthorship' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.WorkAuthorship
    UNION ALL
    SELECT 'WorkAuthorshipInstitution' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.WorkAuthorshipInstitution
    UNION ALL
    SELECT 'WorkMatchedInstitution' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.WorkMatchedInstitution
    UNION ALL
    SELECT 'OperationalLog' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.OperationalLog
) counts
ORDER BY EntityName;
