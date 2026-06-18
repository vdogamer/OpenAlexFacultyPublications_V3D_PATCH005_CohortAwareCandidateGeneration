/*
OpenAlex Version 2C — Optional local/test reset

Use only in a test/local database. This calls the existing V1 reset procedure if available.

Review before running.
*/

-- EXEC openalex.usp_ClearOpenAlexTestLoadData_V1B
--     @AllowClearTestLoadData = 1,
--     @ClearOperationalLogs = 0,
--     @ClearIngestionRunHistory = 0,
--     @BypassDatabaseNameSafety = 0,
--     @RequestedBy = N'Version 2C test reset';
