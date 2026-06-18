/*
OpenAlex Version 2H — Manager Demo Readiness Report
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

DECLARE @Report TABLE
(
    CheckName NVARCHAR(200),
    CheckStatus NVARCHAR(50),
    Details NVARCHAR(MAX)
);

INSERT INTO @Report
SELECT
    'Faculty rows',
    CASE WHEN OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Faculty) THEN 'Ready' ELSE 'Needs data' END,
    'At least one faculty row should exist for demo.';

INSERT INTO @Report
SELECT
    'Institutions',
    CASE WHEN OBJECT_ID(N'openalex.Institution', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Institution) THEN 'Ready' ELSE 'Needs ingestion' END,
    'Run InstitutionIngestion if empty.';

INSERT INTO @Report
SELECT
    'Authors',
    CASE WHEN OBJECT_ID(N'openalex.Author', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Author) THEN 'Ready' ELSE 'Needs ingestion' END,
    'Run AuthorIngestion if empty.';

INSERT INTO @Report
SELECT
    'Works',
    CASE WHEN OBJECT_ID(N'openalex.Work', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.Work) THEN 'Ready' ELSE 'Needs ingestion' END,
    'Run WorkIngestion if empty.';

INSERT INTO @Report
SELECT
    'Author candidates',
    CASE WHEN OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.FacultyAuthorMatchCandidate) THEN 'Ready' ELSE 'Needs generation' END,
    'Run FacultyAuthorCandidateGeneration if empty.';

INSERT INTO @Report
SELECT
    'Publication candidates',
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.FacultyPublicationCandidate) THEN 'Ready' ELSE 'Needs generation' END,
    'Run FacultyPublicationCandidateGeneration after accepting at least one author.';

INSERT INTO @Report
SELECT
    'Accepted publications',
    CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.FacultyPublicationDecision WHERE DecisionStatus = 'Accepted' AND IsCurrent = 1) THEN 'Ready' ELSE 'Needs review' END,
    'Accept at least one publication candidate for report demo.';

INSERT INTO @Report
SELECT
    'Admin user',
    CASE WHEN OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM openalex.AppUser WHERE UserName = 'admin.demo') THEN 'Ready' ELSE 'Needs seed' END,
    'Run V1N auth seed if missing.';

SELECT *
FROM @Report
ORDER BY
    CASE CheckStatus
        WHEN 'Needs data' THEN 1
        WHEN 'Needs ingestion' THEN 2
        WHEN 'Needs generation' THEN 3
        WHEN 'Needs review' THEN 4
        WHEN 'Needs seed' THEN 5
        ELSE 9
    END,
    CheckName;
