/*
Faculty DOI lookup: live comparison (SQL Server 2016+).
Set @InputUserId below, select the app database in SSMS, and run the whole file.
This reads application records; only session-local table variables are written.
Export the FINAL results grid to CSV and retain the schema grid and Messages.
Raw DOI rows intentionally retain duplicates and malformed values so the app's
normalization can be checked separately. No final DOI count is asserted here.
SQL fragments copied from FacultyDoiRepository.cs, SHA256:
803794986cec37f2f37d299c31d2b4b181e57f5739b6167c7424706bf2574a8a
*/
SET NOCOUNT ON;

DECLARE @InputUserId NVARCHAR(64) = N'REPLACE_WITH_USERID';
DECLARE @ExpectedDatabase SYSNAME = N'OpenAlexIngestion_Test';
DECLARE @RunId UNIQUEIDENTIFIER = NEWID();
DECLARE @CapturedAtUtc DATETIME2(3) = SYSUTCDATETIME();
DECLARE @UserId NVARCHAR(64) = LOWER(LTRIM(RTRIM(@InputUserId)));

IF DB_NAME() <> @ExpectedDatabase
BEGIN
    ;THROW 51000, 'Select the application database in SSMS. Update @ExpectedDatabase only if the app uses a different database.', 1;
END;
IF RIGHT(@UserId, 13) = N'@virginia.edu'
    SET @UserId = LEFT(@UserId, LEN(@UserId) - 13);
IF @UserId IS NULL OR LEN(@UserId) NOT BETWEEN 2 AND 32
   OR @UserId COLLATE Latin1_General_100_BIN2 LIKE N'%[^a-z0-9]%'
BEGIN
    ;THROW 51001, 'Set @InputUserId to the faculty ComputingID (2-32 letters or numbers), or its UVA email.', 1;
END;
DECLARE @Email NVARCHAR(512) = @UserId + N'@virginia.edu';

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

DECLARE @Sources TABLE
(
    SourceName NVARCHAR(32) NOT NULL,
    RequiredObjects INT NOT NULL,
    VisibleObjects INT NOT NULL,
    RequiredColumns INT NOT NULL,
    VisibleColumns INT NOT NULL,
    SourceStatus NVARCHAR(32) NOT NULL
);
INSERT INTO @Sources
SELECT r.SourceName,
    COUNT(DISTINCT r.ObjectName),
    COUNT(DISTINCT CASE WHEN OBJECT_ID(r.ObjectName) IS NOT NULL THEN r.ObjectName END),
    COUNT(*), COUNT(c.column_id),
    CASE WHEN COUNT(c.column_id) = COUNT(*) THEN N'Ready'
         WHEN COUNT(DISTINCT CASE WHEN OBJECT_ID(r.ObjectName) IS NOT NULL THEN r.ObjectName END)
              < COUNT(DISTINCT r.ObjectName) THEN N'Unavailable'
         ELSE N'Incompatible columns' END
FROM @Required r
LEFT JOIN sys.columns c
  ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
GROUP BY r.SourceName;

-- Grid 1: the run/database marker ties these schema results to the DOI export.
SELECT @RunId AS ValidationRunId, @CapturedAtUtc AS CapturedAtUtc,
       @@SERVERNAME AS ConnectedServer, DB_NAME() AS ConnectedDatabase,
       @UserId AS UserId, s.*
FROM @Sources s ORDER BY s.SourceName;

-- Grid 2: this grid should have zero rows if every source is installed and ready.
SELECT @RunId AS ValidationRunId, DB_NAME() AS ConnectedDatabase,
       r.SourceName, r.ObjectName, r.ColumnName AS MissingColumn
FROM @Required r
LEFT JOIN sys.columns c
  ON c.object_id = OBJECT_ID(r.ObjectName) AND c.name = r.ColumnName
WHERE c.column_id IS NULL
ORDER BY r.SourceName, r.ObjectName, r.ColumnName;

IF EXISTS (SELECT 1 FROM @Sources WHERE SourceStatus = N'Incompatible columns')
BEGIN
    ;THROW 51002, 'A source has incompatible columns. Share the schema grids; no partial DOI comparison was executed.', 1;
END;
IF NOT EXISTS (SELECT 1 FROM @Sources WHERE SourceStatus = N'Ready')
BEGIN
    ;THROW 51003, 'No lookup source is ready in this database. Share the schema grids; no DOI comparison was executed.', 1;
END;

DECLARE @Sql NVARCHAR(MAX) = N'';

IF EXISTS (SELECT 1 FROM @Sources WHERE SourceName = N'Scholar cache' AND SourceStatus = N'Ready')
BEGIN
    SET @Sql = @Sql + CASE WHEN LEN(@Sql) > 0 THEN CHAR(10) + N'UNION ALL' + CHAR(10) ELSE N'' END
        + N'
SELECT s.DisplayName, p.Doi, p.Title, p.PublicationYear,
       N''Synchronized publications'' AS Source
FROM academiconline.Scholar s
LEFT JOIN
(
    SELECT DISTINCT sp.AcademicOnlineAuthorId, v.PublicationKey,
           v.Doi, v.Title, v.PublicationYear
    FROM academiconline.DepartmentScholarPublication sp
    JOIN academiconline.Department d ON d.DepartmentId = sp.DepartmentId
    JOIN academiconline.vw_DepartmentNewestPublication v
      ON v.DepartmentId = sp.DepartmentId AND v.PublicationKey = sp.PublicationKey
    WHERE sp.IsCurrent = 1
      AND sp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
      AND d.SourceMode <> N''FacultyRoster''
) p ON p.AcademicOnlineAuthorId = s.AcademicOnlineAuthorId
WHERE LOWER(LTRIM(RTRIM(s.UvaUserId))) = @UserId
   OR LOWER(LTRIM(RTRIM(s.Email))) = @Email';
END;

IF EXISTS (SELECT 1 FROM @Sources WHERE SourceName = N'Roster cache' AND SourceStatus = N'Ready')
BEGIN
    SET @Sql = @Sql + CASE WHEN LEN(@Sql) > 0 THEN CHAR(10) + N'UNION ALL' + CHAR(10) ELSE N'' END
        + N'
SELECT rm.DisplayName, p.Doi, p.Title, p.PublicationYear,
       N''Synchronized publications'' AS Source
FROM academiconline.DepartmentRosterMember rm
LEFT JOIN
(
    SELECT DISTINCT rp.DepartmentRosterMemberId, v.PublicationKey,
           v.Doi, v.Title, v.PublicationYear
    FROM academiconline.DepartmentRosterPublication rp
    JOIN academiconline.Department d ON d.DepartmentId = rp.DepartmentId
    JOIN academiconline.vw_DepartmentNewestPublication v
      ON v.DepartmentId = rp.DepartmentId AND v.PublicationKey = rp.PublicationKey
    WHERE rp.IsCurrent = 1
      AND rp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
      AND d.SourceMode = N''FacultyRoster''
) p ON p.DepartmentRosterMemberId = rm.DepartmentRosterMemberId
WHERE rm.IsActive = 1
  AND LOWER(LTRIM(RTRIM(rm.ExpectedEmail))) = @Email
';
END;

IF EXISTS (SELECT 1 FROM @Sources WHERE SourceName = N'Accepted OpenAlex' AND SourceStatus = N'Ready')
BEGIN
    SET @Sql = @Sql + CASE WHEN LEN(@Sql) > 0 THEN CHAR(10) + N'UNION ALL' + CHAR(10) ELSE N'' END
        + N'
SELECT f.DisplayName, p.Doi, p.Title, p.PublicationYear,
       N''Accepted OpenAlex publications'' AS Source
FROM openalex.Faculty f
LEFT JOIN
(
    SELECT d.FacultyId, COALESCE(NULLIF(LTRIM(RTRIM(d.WorkDoi)), N''''), pc.WorkDoi) AS Doi,
           COALESCE(d.WorkTitle, pc.WorkTitle) AS Title,
           COALESCE(d.PublicationYear, pc.PublicationYear) AS PublicationYear
    FROM openalex.FacultyPublicationDecision d
    LEFT JOIN openalex.FacultyPublicationCandidate pc
      ON pc.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
    WHERE d.IsCurrent = 1 AND d.DecisionStatus = N''Accepted''
) p ON p.FacultyId = f.FacultyId
WHERE LOWER(LTRIM(RTRIM(f.PrimaryEmail))) = @Email';
    IF COL_LENGTH(N'openalex.Faculty', N'SourceFacultyKey') IS NOT NULL
        SET @Sql = @Sql + N' OR LOWER(LTRIM(RTRIM(f.SourceFacultyKey))) = @UserId';
END;

DECLARE @Rows TABLE
(
    FacultyName NVARCHAR(MAX) NULL,
    RawDoi NVARCHAR(MAX) NULL,
    Title NVARCHAR(MAX) NULL,
    PublicationYear INT NULL,
    SourceName NVARCHAR(128) NOT NULL
);
INSERT INTO @Rows (FacultyName, RawDoi, Title, PublicationYear, SourceName)
EXEC sys.sp_executesql @Sql,
    N'@UserId NVARCHAR(32), @Email NVARCHAR(512)',
    @UserId = @UserId, @Email = @Email;

DECLARE @MatchedRows BIGINT = (SELECT COUNT_BIG(*) FROM @Rows);
DECLARE @RawDoiRows BIGINT =
    (SELECT COUNT_BIG(*) FROM @Rows WHERE NULLIF(LTRIM(RTRIM(RawDoi)), N'') IS NOT NULL);
PRINT N'Validation run: ' + CONVERT(NVARCHAR(36), @RunId);
PRINT N'Matched source rows (includes duplicate and no-DOI rows): ' + CONVERT(NVARCHAR(30), @MatchedRows);
PRINT N'Nonblank raw DOI rows (not a unique valid DOI count): ' + CONVERT(NVARCHAR(30), @RawDoiRows);
IF @MatchedRows = 0
    PRINT N'No faculty record matched this userid in the ready sources. This is not a confirmed zero-publication result.';
IF EXISTS (SELECT 1 FROM @Sources WHERE SourceStatus <> N'Ready')
    PRINT N'One or more sources are unavailable. Review the schema grid before assessing completeness.';

-- FINAL grid: export THIS grid to CSV, even when some rows have no RawDoi.
-- The identity columns let us confirm the userid resolves to the intended person.
SELECT @RunId AS ValidationRunId, DB_NAME() AS ConnectedDatabase,
       @UserId AS UserId, FacultyName, RawDoi, Title, PublicationYear, SourceName
FROM @Rows
ORDER BY PublicationYear DESC, RawDoi, FacultyName, SourceName;
