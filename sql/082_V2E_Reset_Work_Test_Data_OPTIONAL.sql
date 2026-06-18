/*
OpenAlex Version 2E — Optional local/test work reset

Review before running. Use only in a local/test database.
*/

-- DELETE FROM openalex.WorkMatchedInstitution;
-- DELETE FROM openalex.WorkAuthorshipInstitution;
-- DELETE FROM openalex.WorkAuthorship;
-- DELETE FROM openalex.Work;

-- Optional: clear only Work request logs from test runs.
-- DELETE FROM openalex.RequestLog WHERE EntityType = 'Work';
