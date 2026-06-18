/*
OpenAlex Version 2I — Production Readiness Report
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

DECLARE @Report TABLE
(
    Area NVARCHAR(100),
    CheckName NVARCHAR(200),
    CheckStatus NVARCHAR(50),
    Details NVARCHAR(MAX)
);

INSERT INTO @Report
SELECT 'Schema', 'OperationalLog table',
    CASE WHEN OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL THEN 'Ready' ELSE 'Missing' END,
    'Required for operational dashboard and worker evidence.';

INSERT INTO @Report
SELECT 'Schema', 'IngestionRun table',
    CASE WHEN OBJECT_ID(N'openalex.IngestionRun', N'U') IS NOT NULL THEN 'Ready' ELSE 'Missing' END,
    'Required for run history.';

INSERT INTO @Report
SELECT 'Schema', 'RequestLog table',
    CASE WHEN OBJECT_ID(N'openalex.RequestLog', N'U') IS NOT NULL THEN 'Ready' ELSE 'Missing' END,
    'Required for API request audit.';

INSERT INTO @Report
SELECT 'Schema', 'Auth tables',
    CASE WHEN OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
          AND OBJECT_ID(N'openalex.AppRole', N'U') IS NOT NULL
          AND OBJECT_ID(N'openalex.AppUserRole', N'U') IS NOT NULL
         THEN 'Ready' ELSE 'Missing' END,
    'Required for Reader/Reviewer/Admin access.';

INSERT INTO @Report
SELECT 'Data', 'Faculty data',
    CASE WHEN OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Faculty) THEN 'Ready' ELSE 'Needs data' END,
    'Faculty source mirror/staging table should have active users.';

INSERT INTO @Report
SELECT 'Data', 'OpenAlex authors',
    CASE WHEN OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Author) THEN 'Ready' ELSE 'Needs ingestion' END,
    'Run AuthorIngestion if empty.';

INSERT INTO @Report
SELECT 'Data', 'OpenAlex works',
    CASE WHEN OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Work) THEN 'Ready' ELSE 'Needs ingestion' END,
    'Run WorkIngestion if empty.';

INSERT INTO @Report
SELECT 'Workflow', 'Author candidates',
    CASE WHEN OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.FacultyAuthorMatchCandidate) THEN 'Ready' ELSE 'Needs generation' END,
    'Run FacultyAuthorCandidateGeneration if empty.';

INSERT INTO @Report
SELECT 'Workflow', 'Publication candidates',
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.FacultyPublicationCandidate) THEN 'Ready' ELSE 'Needs generation/review' END,
    'Accept author candidates before publication candidate generation.';

INSERT INTO @Report
SELECT 'Workflow', 'Accepted publications',
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NOT NULL
          AND EXISTS (SELECT 1 FROM openalex.FacultyPublicationDecision WHERE DecisionStatus = 'Accepted' AND IsCurrent = 1)
         THEN 'Ready' ELSE 'Needs review' END,
    'Accept at least one publication candidate for final demo.';

INSERT INTO @Report
SELECT 'Security', 'Admin demo user',
    CASE WHEN OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.AppUser WHERE UserName = 'admin.demo') THEN 'Ready' ELSE 'Needs seed' END,
    'admin.demo should exist for local manager demo.';

SELECT *
FROM @Report
ORDER BY
    CASE CheckStatus
        WHEN 'Missing' THEN 1
        WHEN 'Needs data' THEN 2
        WHEN 'Needs ingestion' THEN 3
        WHEN 'Needs generation' THEN 4
        WHEN 'Needs generation/review' THEN 5
        WHEN 'Needs review' THEN 6
        WHEN 'Needs seed' THEN 7
        ELSE 9
    END,
    Area,
    CheckName;
