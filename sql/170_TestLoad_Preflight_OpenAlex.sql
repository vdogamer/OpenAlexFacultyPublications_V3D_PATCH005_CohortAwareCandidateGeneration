/*
Controlled OpenAlex TestLoad — Preflight

Run before pulling real OpenAlex data.
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

DECLARE @Checks TABLE
(
    CheckName NVARCHAR(200),
    CheckStatus NVARCHAR(50),
    Details NVARCHAR(MAX)
);

INSERT INTO @Checks
SELECT
    'Not system database',
    CASE WHEN DB_NAME() NOT IN ('master','model','msdb','tempdb') THEN 'Ready' ELSE 'Blocker' END,
    'Do not run TestLoad against a system database.';

INSERT INTO @Checks
SELECT
    'Ingestion tables exist',
    CASE WHEN OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL
          AND OBJECT_ID(N'openalex.RequestLog', N'U') IS NOT NULL
         THEN 'Ready' ELSE 'Blocker' END,
    'Required before worker ingestion.';

INSERT INTO @Checks
SELECT
    'OpenAlex entity tables exist',
    CASE WHEN OBJECT_ID(N'openalex.Institution', N'U') IS NOT NULL
          AND OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL
          AND OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL
         THEN 'Ready' ELSE 'Blocker' END,
    'Required for institution, author, and work ingestion.';

INSERT INTO @Checks
SELECT
    'Faculty rows exist',
    CASE WHEN OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL
          AND EXISTS (SELECT 1 FROM openalex.Faculty)
         THEN 'Ready' ELSE 'Needs data' END,
    'Faculty rows are required before candidate generation.';

INSERT INTO @Checks
SELECT
    'Scheduler tables exist',
    CASE WHEN OBJECT_ID(N'openalex.JobDefinition', N'U') IS NOT NULL
          AND OBJECT_ID(N'openalex.JobExecutionRequest', N'U') IS NOT NULL
         THEN 'Ready' ELSE 'Warning' END,
    'Required only if using the queued scheduler path.';

SELECT *
FROM @Checks
ORDER BY
    CASE CheckStatus
        WHEN 'Blocker' THEN 1
        WHEN 'Needs data' THEN 2
        WHEN 'Warning' THEN 3
        ELSE 9
    END,
    CheckName;
