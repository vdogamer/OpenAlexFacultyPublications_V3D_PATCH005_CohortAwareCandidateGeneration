/*
OpenAlex Version 2D — Optional local/test author reset

Review before running. This clears author data only when used in a test/local database.
*/

-- DELETE FROM openalex.AuthorLastKnownInstitution;
-- DELETE FROM openalex.Author;

-- Optional: clear only Author request logs from test runs.
-- DELETE FROM openalex.RequestLog WHERE EntityType = 'Author';
