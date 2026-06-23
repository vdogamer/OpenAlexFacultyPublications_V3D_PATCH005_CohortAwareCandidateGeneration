/*
V3E PATCH005 - Update Jeff Martens ORCID and OpenAlex ID
SQL Server 2016-safe.

Purpose:
  Add verified identifiers to Jeff/Jeffrey Martens' openalex.Faculty row.

Important:
  This updates the faculty seed data only.
  It does NOT insert accepted identity decisions.
  It does NOT insert accepted publications.

Before running:
  Replace the two placeholder values below.

Accepted formats:
  @VerifiedOrcid may be either:
    0000-0000-0000-0000
    https://orcid.org/0000-0000-0000-0000

  @VerifiedOpenAlexAuthorId may be either:
    A1234567890
    https://openalex.org/A1234567890
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DisplayName NVARCHAR(512) = N'Jeffrey Martens';
DECLARE @AlternateDisplayName NVARCHAR(512) = N'Jeff Martens';
DECLARE @FirstName NVARCHAR(200) = N'Jeffrey';
DECLARE @ShortFirstName NVARCHAR(200) = N'Jeff';
DECLARE @LastName NVARCHAR(200) = N'Martens';

-- TODO: Replace these placeholder values before running.
DECLARE @VerifiedOrcid NVARCHAR(256) = N'https://orcid.org/0000-0000-0000-0000';
DECLARE @VerifiedOpenAlexAuthorId NVARCHAR(128) = N'https://openalex.org/A0000000000';

DECLARE @FacultyId BIGINT;
DECLARE @DisplayColumn SYSNAME;
DECLARE @OrcidColumn SYSNAME;
DECLARE @OpenAlexColumn SYSNAME;
DECLARE @sql NVARCHAR(MAX);
DECLARE @params NVARCHAR(MAX);

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'openalex.Faculty', N'FacultyId') IS NULL
BEGIN
    RAISERROR('openalex.Faculty.FacultyId does not exist.', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'openalex.Faculty', N'DisplayName') IS NOT NULL SET @DisplayColumn = N'DisplayName';
ELSE IF COL_LENGTH(N'openalex.Faculty', N'FacultyDisplayName') IS NOT NULL SET @DisplayColumn = N'FacultyDisplayName';
ELSE IF COL_LENGTH(N'openalex.Faculty', N'FullName') IS NOT NULL SET @DisplayColumn = N'FullName';
ELSE IF COL_LENGTH(N'openalex.Faculty', N'Name') IS NOT NULL SET @DisplayColumn = N'Name';

IF @DisplayColumn IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not have a recognizable display-name column.', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'openalex.Faculty', N'Orcid') IS NOT NULL SET @OrcidColumn = N'Orcid';
ELSE IF COL_LENGTH(N'openalex.Faculty', N'ORCID') IS NOT NULL SET @OrcidColumn = N'ORCID';

IF COL_LENGTH(N'openalex.Faculty', N'ClaimedOpenAlexAuthorId') IS NOT NULL SET @OpenAlexColumn = N'ClaimedOpenAlexAuthorId';
ELSE IF COL_LENGTH(N'openalex.Faculty', N'OpenAlexAuthorId') IS NOT NULL SET @OpenAlexColumn = N'OpenAlexAuthorId';
ELSE IF COL_LENGTH(N'openalex.Faculty', N'AuthorOpenAlexId') IS NOT NULL SET @OpenAlexColumn = N'AuthorOpenAlexId';

IF @OrcidColumn IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not have an ORCID column. Expected Orcid or ORCID.', 16, 1);
    RETURN;
END;

IF @OpenAlexColumn IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not have a claimed OpenAlex author ID column. Expected ClaimedOpenAlexAuthorId, OpenAlexAuthorId, or AuthorOpenAlexId.', 16, 1);
    RETURN;
END;

IF @VerifiedOrcid IS NULL OR @VerifiedOrcid LIKE N'%0000-0000-0000-0000%'
BEGIN
    RAISERROR('Replace @VerifiedOrcid with Jeff Martens'' verified ORCID before running this script.', 16, 1);
    RETURN;
END;

IF @VerifiedOpenAlexAuthorId IS NULL OR @VerifiedOpenAlexAuthorId LIKE N'%A0000000000%'
BEGIN
    RAISERROR('Replace @VerifiedOpenAlexAuthorId with Jeff Martens'' verified OpenAlex author ID before running this script.', 16, 1);
    RETURN;
END;

-- Normalize common OpenAlex input to the full OpenAlex URL format used by the faculty seed column.
IF @VerifiedOpenAlexAuthorId NOT LIKE N'https://openalex.org/A%'
BEGIN
    IF @VerifiedOpenAlexAuthorId LIKE N'A%'
    BEGIN
        SET @VerifiedOpenAlexAuthorId = N'https://openalex.org/' + @VerifiedOpenAlexAuthorId;
    END;
END;

-- Normalize common ORCID input to full ORCID URL format.
IF @VerifiedOrcid NOT LIKE N'https://orcid.org/%'
BEGIN
    SET @VerifiedOrcid = N'https://orcid.org/' + @VerifiedOrcid;
END;

SET @sql = N'
SELECT TOP (1) @FacultyIdOut = FacultyId
FROM openalex.Faculty
WHERE ' + QUOTENAME(@DisplayColumn) + N' IN (@DisplayName, @AlternateDisplayName)';

IF COL_LENGTH(N'openalex.Faculty', N'FirstName') IS NOT NULL
   AND COL_LENGTH(N'openalex.Faculty', N'LastName') IS NOT NULL
BEGIN
    SET @sql = @sql + N'
   OR (LastName = @LastName AND FirstName IN (@FirstName, @ShortFirstName))';
END;

SET @sql = @sql + N'
ORDER BY FacultyId;';

SET @params = N'@DisplayName NVARCHAR(512), @AlternateDisplayName NVARCHAR(512), @FirstName NVARCHAR(200), @ShortFirstName NVARCHAR(200), @LastName NVARCHAR(200), @FacultyIdOut BIGINT OUTPUT';
EXEC sp_executesql
    @sql,
    @params,
    @DisplayName = @DisplayName,
    @AlternateDisplayName = @AlternateDisplayName,
    @FirstName = @FirstName,
    @ShortFirstName = @ShortFirstName,
    @LastName = @LastName,
    @FacultyIdOut = @FacultyId OUTPUT;

IF @FacultyId IS NULL
BEGIN
    RAISERROR('Could not find Jeff/Jeffrey Martens in openalex.Faculty. Run sql/233_V3E_PATCH004_AddJeffMartens_Pilot16_SQL2016_v2.sql first.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

SET @sql = N'
UPDATE openalex.Faculty
SET ' + QUOTENAME(@OrcidColumn) + N' = @VerifiedOrcid,
    ' + QUOTENAME(@OpenAlexColumn) + N' = @VerifiedOpenAlexAuthorId';

IF COL_LENGTH(N'openalex.Faculty', N'UpdatedAtUtc') IS NOT NULL
BEGIN
    SET @sql = @sql + N',
    UpdatedAtUtc = SYSUTCDATETIME()';
END;

IF COL_LENGTH(N'openalex.Faculty', N'LastSeenAtUtc') IS NOT NULL
BEGIN
    SET @sql = @sql + N',
    LastSeenAtUtc = SYSUTCDATETIME()';
END;

SET @sql = @sql + N'
WHERE FacultyId = @FacultyId;';

EXEC sp_executesql
    @sql,
    N'@FacultyId BIGINT, @VerifiedOrcid NVARCHAR(256), @VerifiedOpenAlexAuthorId NVARCHAR(128)',
    @FacultyId = @FacultyId,
    @VerifiedOrcid = @VerifiedOrcid,
    @VerifiedOpenAlexAuthorId = @VerifiedOpenAlexAuthorId;

COMMIT TRANSACTION;

PRINT 'Jeff Martens identifiers updated.';

SET @sql = N'
SELECT
    FacultyId,
    ' + QUOTENAME(@DisplayColumn) + N' AS DisplayName,
    ' + QUOTENAME(@OrcidColumn) + N' AS Orcid,
    ' + QUOTENAME(@OpenAlexColumn) + N' AS ClaimedOpenAlexAuthorId
FROM openalex.Faculty
WHERE FacultyId = @FacultyId;';

EXEC sp_executesql
    @sql,
    N'@FacultyId BIGINT',
    @FacultyId = @FacultyId;
