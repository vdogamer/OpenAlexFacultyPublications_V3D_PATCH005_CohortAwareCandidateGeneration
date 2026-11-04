/*
Faculty DOI Lookup: live database preflight (SQL Server 2016+).
Run in SSMS on the same database used by OpenAlex.Web.
Reads metadata and up to ten candidate identities per source. No application
tables, views, procedures, or publication rows are created or changed.
This is a readiness check, not a substitute for testing the page's DOI results.
*/
SET NOCOUNT ON;

DECLARE @ExpectedDatabase SYSNAME = N'OpenAlexIngestion_Test';
IF DB_NAME() <> @ExpectedDatabase
BEGIN
    ;THROW 51000, 'Select OpenAlexIngestion_Test in SSMS before running this preflight. If your app uses another database, update @ExpectedDatabase first.', 1;
END;

SELECT
    @@SERVERNAME AS ConnectedServer,
    DB_NAME() AS ConnectedDatabase,
    CONVERT(NVARCHAR(128), SERVERPROPERTY('ProductVersion')) AS SqlServerVersion;

DECLARE @Required TABLE
(
    SourceName NVARCHAR(32) NOT NULL,
    ObjectName NVARCHAR(256) NOT NULL,
    ColumnName SYSNAME NOT NULL
);

INSERT INTO @Required (SourceName, ObjectName, ColumnName)
VALUES
(N'Scholar cache', N'academiconline.Scholar', N'AcademicOnlineAuthorId'),
(N'Scholar cache', N'academiconline.Scholar', N'DisplayName'),
(N'Scholar cache', N'academiconline.Scholar', N'UvaUserId'),
(N'Scholar cache', N'academiconline.Scholar', N'Email'),
(N'Scholar cache', N'academiconline.Department', N'DepartmentId'),
(N'Scholar cache', N'academiconline.Department', N'CurrentSnapshotRunId'),
(N'Scholar cache', N'academiconline.Department', N'SourceMode'),
(N'Scholar cache', N'academiconline.DepartmentScholarPublication', N'DepartmentId'),
(N'Scholar cache', N'academiconline.DepartmentScholarPublication', N'AcademicOnlineAuthorId'),
(N'Scholar cache', N'academiconline.DepartmentScholarPublication', N'PublicationKey'),
(N'Scholar cache', N'academiconline.DepartmentScholarPublication', N'IsCurrent'),
(N'Scholar cache', N'academiconline.DepartmentScholarPublication', N'CurrentSnapshotRunId'),
(N'Scholar cache', N'academiconline.vw_DepartmentNewestPublication', N'DepartmentId'),
(N'Scholar cache', N'academiconline.vw_DepartmentNewestPublication', N'PublicationKey'),
(N'Scholar cache', N'academiconline.vw_DepartmentNewestPublication', N'Doi'),
(N'Scholar cache', N'academiconline.vw_DepartmentNewestPublication', N'Title'),
(N'Scholar cache', N'academiconline.vw_DepartmentNewestPublication', N'PublicationYear'),
(N'Roster cache', N'academiconline.DepartmentRosterMember', N'DepartmentRosterMemberId'),
(N'Roster cache', N'academiconline.DepartmentRosterMember', N'DisplayName'),
(N'Roster cache', N'academiconline.DepartmentRosterMember', N'ExpectedEmail'),
(N'Roster cache', N'academiconline.DepartmentRosterMember', N'IsActive'),
(N'Roster cache', N'academiconline.Department', N'DepartmentId'),
(N'Roster cache', N'academiconline.Department', N'CurrentSnapshotRunId'),
(N'Roster cache', N'academiconline.Department', N'SourceMode'),
(N'Roster cache', N'academiconline.DepartmentRosterPublication', N'DepartmentRosterMemberId'),
(N'Roster cache', N'academiconline.DepartmentRosterPublication', N'DepartmentId'),
(N'Roster cache', N'academiconline.DepartmentRosterPublication', N'PublicationKey'),
(N'Roster cache', N'academiconline.DepartmentRosterPublication', N'IsCurrent'),
(N'Roster cache', N'academiconline.DepartmentRosterPublication', N'CurrentSnapshotRunId'),
(N'Roster cache', N'academiconline.vw_DepartmentNewestPublication', N'DepartmentId'),
(N'Roster cache', N'academiconline.vw_DepartmentNewestPublication', N'PublicationKey'),
(N'Roster cache', N'academiconline.vw_DepartmentNewestPublication', N'Doi'),
(N'Roster cache', N'academiconline.vw_DepartmentNewestPublication', N'Title'),
(N'Roster cache', N'academiconline.vw_DepartmentNewestPublication', N'PublicationYear'),
(N'Accepted OpenAlex', N'openalex.Faculty', N'FacultyId'),
(N'Accepted OpenAlex', N'openalex.Faculty', N'DisplayName'),
(N'Accepted OpenAlex', N'openalex.Faculty', N'PrimaryEmail'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'FacultyId'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'FacultyPublicationCandidateId'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'WorkDoi'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'WorkTitle'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'PublicationYear'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'IsCurrent'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationDecision', N'DecisionStatus'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationCandidate', N'FacultyPublicationCandidateId'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationCandidate', N'WorkDoi'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationCandidate', N'WorkTitle'),
(N'Accepted OpenAlex', N'openalex.FacultyPublicationCandidate', N'PublicationYear');

-- Result 2: every configured source should be Ready. Entirely absent sources
-- can be optional; missing columns in an existing source require investigation.
SELECT r.SourceName,
    CASE WHEN COUNT(c.column_id) = COUNT(*)
         THEN N'Ready' ELSE N'Missing or not visible' END AS SchemaStatus,
    COUNT(*) AS RequiredColumnCount,
    COUNT(c.column_id) AS VisibleColumnCount
FROM @Required r
LEFT JOIN sys.columns c
  ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
GROUP BY r.SourceName
ORDER BY r.SourceName;

-- Result 3: an empty result means every required column is visible.
SELECT r.SourceName, r.ObjectName, r.ColumnName AS MissingColumn
FROM @Required r
LEFT JOIN sys.columns c
  ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
WHERE c.column_id IS NULL
ORDER BY r.SourceName, r.ObjectName, r.ColumnName;

-- SourceFacultyKey is deliberately optional in the application.
SELECT CASE WHEN COL_LENGTH(N'openalex.Faculty', N'SourceFacultyKey') IS NULL
            THEN N'Unavailable; matching uses PrimaryEmail'
            ELSE N'Available for exact ComputingID matching' END AS SourceFacultyKeyStatus;

-- Dynamic batches avoid binding references to sources that are not installed.
IF NOT EXISTS
(
    SELECT 1 FROM @Required r
    LEFT JOIN sys.columns c
      ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
    WHERE r.SourceName = N'Scholar cache' AND c.column_id IS NULL
)
BEGIN
    EXEC sys.sp_executesql N'
SELECT TOP (10)
    N''Scholar cache'' AS SourceName,
    COALESCE(NULLIF(LOWER(LTRIM(RTRIM(UvaUserId))), N''''),
        CASE WHEN RIGHT(LOWER(LTRIM(RTRIM(Email))), 13) = N''@virginia.edu''
             THEN LEFT(LOWER(LTRIM(RTRIM(Email))), LEN(LTRIM(RTRIM(Email))) - 13) END) AS CandidateUserId,
    DisplayName AS FacultyName
FROM academiconline.Scholar
WHERE NULLIF(LTRIM(RTRIM(UvaUserId)), N'''') IS NOT NULL
   OR RIGHT(LOWER(LTRIM(RTRIM(Email))), 13) = N''@virginia.edu''
ORDER BY DisplayName, AcademicOnlineAuthorId;';
END;

IF NOT EXISTS
(
    SELECT 1 FROM @Required r
    LEFT JOIN sys.columns c
      ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
    WHERE r.SourceName = N'Roster cache' AND c.column_id IS NULL
)
BEGIN
    EXEC sys.sp_executesql N'
SELECT TOP (10)
    N''Roster cache'' AS SourceName,
    LEFT(LOWER(LTRIM(RTRIM(ExpectedEmail))), LEN(LTRIM(RTRIM(ExpectedEmail))) - 13) AS CandidateUserId,
    DisplayName AS FacultyName
FROM academiconline.DepartmentRosterMember
WHERE IsActive = 1
  AND RIGHT(LOWER(LTRIM(RTRIM(ExpectedEmail))), 13) = N''@virginia.edu''
ORDER BY DisplayName, DepartmentRosterMemberId;';
END;

IF NOT EXISTS
(
    SELECT 1 FROM @Required r
    LEFT JOIN sys.columns c
      ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
    WHERE r.SourceName = N'Accepted OpenAlex' AND c.column_id IS NULL
)
BEGIN
    EXEC sys.sp_executesql N'
SELECT TOP (10)
    N''Accepted OpenAlex'' AS SourceName,
    LEFT(LOWER(LTRIM(RTRIM(PrimaryEmail))), LEN(LTRIM(RTRIM(PrimaryEmail))) - 13) AS CandidateUserId,
    DisplayName AS FacultyName
FROM openalex.Faculty
WHERE RIGHT(LOWER(LTRIM(RTRIM(PrimaryEmail))), 13) = N''@virginia.edu''
ORDER BY DisplayName, FacultyId;';
END;

PRINT N'Preflight finished. Review all schema statuses and missing columns; select a known faculty userid for the next DOI comparison. Live DOI results have not yet been validated.';
