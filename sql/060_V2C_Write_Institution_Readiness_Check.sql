/*
OpenAlex Version 2C — Write-enabled Institution Ingestion Readiness Check
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
        (N'openalex.InstitutionIds', N'U'),
        (N'openalex.OperationalLog', N'U')
) required(ObjectName, ObjectType)
ORDER BY ObjectName;

PRINT 'Allowed IngestionRun RunMode values from V1A are SmokeTest, TestLoad, FullLoad.';
PRINT 'Version 2C defaults to TestLoad.';

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
    SELECT 'InstitutionIds' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.InstitutionIds
    UNION ALL
    SELECT 'OperationalLog' AS EntityName, COUNT_BIG(*) AS TotalRows FROM openalex.OperationalLog
) counts
ORDER BY EntityName;
