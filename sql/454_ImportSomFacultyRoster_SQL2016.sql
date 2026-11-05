/*
Permanent roster bootstrap, SQL Server 2016+.
Requires session-local inputs from 453 and a validated payload. Run in
OpenAlexIngestion_Test. The roster is separate from provider-specific identities.
Only FacultyRoster, FacultyRosterImportRun and FacultyRosterImportRow are written.
No existing faculty, provider IDs, publication decisions, cohorts or jobs change.
Existing roster keys are reused only when their source fingerprints agree.
Preview creates no permanent objects. Apply is atomic and can be rerun.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
IF @@TRANCOUNT <> 0 THROW 51400, 'Run the roster loader outside an existing transaction.', 1;
IF DB_NAME() <> N'OpenAlexIngestion_Test' THROW 51401, 'Select OpenAlexIngestion_Test.', 1;
IF OBJECT_ID(N'tempdb..#SomRosterContext') IS NULL OR OBJECT_ID(N'tempdb..#SomRosterInput') IS NULL
    THROW 51402, 'The roster context or input table is missing.', 1;
IF (SELECT COUNT(*) FROM #SomRosterContext) <> 1 THROW 51403, 'Exactly one roster context is required.', 1;

DECLARE @SR_Preview bit, @SR_Expected int, @SR_Accepted int, @SR_Quarantined int;
DECLARE @SR_SourceServer nvarchar(128), @SR_SourceDatabase nvarchar(128);
DECLARE @SR_SourceRun uniqueidentifier, @SR_Captured datetime2(7), @SR_EvidenceRun uniqueidentifier;
DECLARE @SR_ExpectedHash varbinary(32), @SR_ActualHash varbinary(32), @SR_Payload nvarchar(max);
DECLARE @SR_Run uniqueidentifier = NEWID(), @SR_Now datetime2(7) = SYSUTCDATETIME();
DECLARE @SR_Lock int, @SR_Inserted int = 0, @SR_Reused int = 0, @SR_Verified int = 0;
DECLARE @SR_NativeBefore bigint, @SR_NativeAfter bigint;
SELECT @SR_Preview=PreviewOnly, @SR_Expected=ExpectedCandidates,
       @SR_Accepted=ExpectedAccepted, @SR_Quarantined=ExpectedQuarantined,
       @SR_SourceServer=SourceServer, @SR_SourceDatabase=SourceDatabase,
       @SR_SourceRun=SourceExportRunId, @SR_Captured=SourceCapturedAtUtc,
       @SR_EvidenceRun=IdentityEvidenceExportRunId, @SR_ExpectedHash=ExpectedPayloadHash
FROM #SomRosterContext;

IF @SR_Expected < 1 OR @SR_Accepted < 1 OR @SR_Quarantined < 0
   OR @SR_Expected <> @SR_Accepted + @SR_Quarantined
   OR @SR_SourceDatabase <> N'ResearchFacultyDirectory'
    THROW 51404, 'Invalid roster context or scope counts.', 1;
IF (SELECT COUNT(*) FROM #SomRosterInput) <> @SR_Expected
    THROW 51405, 'The input row count does not match the validated payload.', 1;
IF EXISTS (SELECT LOWER(LTRIM(RTRIM(SourceUserName))) FROM #SomRosterInput
           GROUP BY LOWER(LTRIM(RTRIM(SourceUserName))) HAVING COUNT(*) > 1)
    THROW 51406, 'Duplicate source usernames were found.', 1;
IF EXISTS (SELECT 1 FROM #SomRosterInput
           WHERE Disposition NOT IN (N'Import',N'Quarantine')
              OR PrimarySchoolCode <> N'MD' OR FacultyClassification <> N'Faculty'
              OR NULLIF(LTRIM(RTRIM(DisplayName)),N'') IS NULL
              OR NULLIF(LTRIM(RTRIM(SourceUserName)),N'') IS NULL)
    THROW 51407, 'An input row has invalid scope, disposition or identity fields.', 1;
IF (SELECT COUNT(*) FROM #SomRosterInput WHERE Disposition=N'Import') <> @SR_Accepted
 OR (SELECT COUNT(*) FROM #SomRosterInput WHERE Disposition=N'Quarantine') <> @SR_Quarantined
    THROW 51408, 'Accepted/quarantine counts differ from the validated context.', 1;
IF EXISTS (SELECT 1 FROM #SomRosterInput WHERE Disposition=N'Import'
           AND (ComputingId IS NULL OR LEN(ComputingId)<2
                OR ComputingId COLLATE Latin1_General_100_BIN2 LIKE N'%[^a-z0-9]%'
                OR ComputingId COLLATE Latin1_General_100_BIN2 <>
                   LOWER(LTRIM(RTRIM(SourceUserName))) COLLATE Latin1_General_100_BIN2))
    THROW 51409, 'An accepted row has an invalid or inconsistent ComputingID.', 1;
IF EXISTS (SELECT ComputingId FROM #SomRosterInput WHERE Disposition=N'Import'
           GROUP BY ComputingId HAVING COUNT(*)>1)
    THROW 51410, 'Accepted ComputingIDs must be unique.', 1;
IF EXISTS (SELECT 1 FROM #SomRosterInput WHERE Disposition=N'Quarantine'
           AND (ComputingId IS NOT NULL OR NULLIF(ValidationNote,N'') IS NULL))
    THROW 51411, 'Quarantined records need a reason and cannot receive a ComputingID.', 1;
IF EXISTS (SELECT 1 FROM #SomRosterInput WHERE SourceRecordHash <>
    HASHBYTES('SHA2_256',CONCAT(CAST(N'' AS nvarchar(max)),SourceUserName,NCHAR(31),
      ComputingId,NCHAR(31),DisplayName,NCHAR(31),FirstName,NCHAR(31),LastName,NCHAR(31),
      PrimarySchoolCode,NCHAR(31),FacultyClassification,NCHAR(31),DepartmentCode,NCHAR(31),
      OrganizationName,NCHAR(31),AcademicRank,NCHAR(31),AppointmentTitle,NCHAR(31),
      RawDirectoryEmail,NCHAR(31),DirectoryEmail,NCHAR(31),
      CONVERT(nvarchar(32),DirectoryPersonId),NCHAR(31),DirectoryAccountStatus,NCHAR(31),
      BaselineIdentityStatus,NCHAR(31),Disposition,NCHAR(31),ValidationNote)))
    THROW 51412, 'A source row fingerprint is invalid; regenerate the payload.', 1;
SELECT @SR_Payload=(SELECT LOWER(LTRIM(RTRIM(i.SourceUserName))) + N'|' +
    CONVERT(nvarchar(64),i.SourceRecordHash,2) + N';'
    FROM #SomRosterInput i ORDER BY LOWER(LTRIM(RTRIM(i.SourceUserName))) COLLATE Latin1_General_100_BIN2
    FOR XML PATH(N''),TYPE).value(N'.',N'nvarchar(max)');
SET @SR_ActualHash=HASHBYTES('SHA2_256',@SR_Payload);
IF @SR_ActualHash<>@SR_ExpectedHash THROW 51413, 'The full roster fingerprint is invalid.', 1;
IF OBJECT_ID(N'openalex.Faculty',N'U') IS NULL OR COL_LENGTH(N'openalex.Faculty',N'FacultyId') IS NULL
    THROW 51414, 'The application faculty table is missing or not visible.', 1;

IF OBJECT_ID(N'tempdb..#SomRosterNative') IS NOT NULL DROP TABLE #SomRosterNative;
IF OBJECT_ID(N'tempdb..#SomRosterExisting') IS NOT NULL DROP TABLE #SomRosterExisting;
IF OBJECT_ID(N'tempdb..#SomRosterCandidates') IS NOT NULL DROP TABLE #SomRosterCandidates;
IF OBJECT_ID(N'tempdb..#SomRosterPlan') IS NOT NULL DROP TABLE #SomRosterPlan;
CREATE TABLE #SomRosterNative(FacultyId bigint PRIMARY KEY,DisplayName nvarchar(512),Email nvarchar(512),SourceKey nvarchar(512));
CREATE TABLE #SomRosterExisting(ComputingId nvarchar(32) PRIMARY KEY,SourceRecordHash varbinary(32),NativeFacultyId bigint NULL,NativeLinkStatus nvarchar(32));
CREATE TABLE #SomRosterCandidates(ComputingId nvarchar(32),FacultyId bigint,PRIMARY KEY(ComputingId,FacultyId));
CREATE TABLE #SomRosterPlan(ComputingId nvarchar(32) PRIMARY KEY,AlreadyLoaded bit,NativeFacultyId bigint NULL,NativeLinkStatus nvarchar(32));

BEGIN TRY
    BEGIN TRANSACTION;
    EXEC @SR_Lock=sys.sp_getapplock @Resource=N'SOM Faculty Roster Import',
        @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
    IF @SR_Lock<0 THROW 51415, 'Another roster import holds the import lock.', 1;

    DECLARE @SR_Name sysname, @SR_Email sysname, @SR_Key sysname, @SR_Sql nvarchar(max);
    SELECT TOP(1) @SR_Name=c.name FROM sys.columns c
    WHERE c.object_id=OBJECT_ID(N'openalex.Faculty') AND c.name IN (N'DisplayName',N'FacultyDisplayName',N'FullName',N'Name')
    ORDER BY CASE c.name WHEN N'DisplayName' THEN 1 WHEN N'FacultyDisplayName' THEN 2 WHEN N'FullName' THEN 3 ELSE 4 END;
    SELECT TOP(1) @SR_Email=c.name FROM sys.columns c
    WHERE c.object_id=OBJECT_ID(N'openalex.Faculty') AND c.name IN (N'PrimaryEmail',N'Email')
    ORDER BY CASE c.name WHEN N'PrimaryEmail' THEN 1 ELSE 2 END;
    IF COL_LENGTH(N'openalex.Faculty',N'SourceFacultyKey') IS NOT NULL SET @SR_Key=N'SourceFacultyKey';
    IF @SR_Name IS NULL THROW 51416, 'No supported faculty display-name column exists.', 1;
    SET @SR_Sql=N'INSERT #SomRosterNative SELECT CONVERT(bigint,FacultyId),CONVERT(nvarchar(512),'+QUOTENAME(@SR_Name)+N'),'+
      CASE WHEN @SR_Email IS NULL THEN N'NULL' ELSE N'LOWER(LTRIM(RTRIM(CONVERT(nvarchar(512),'+QUOTENAME(@SR_Email)+N'))))' END+N','+
      CASE WHEN @SR_Key IS NULL THEN N'NULL' ELSE N'LOWER(LTRIM(RTRIM(CONVERT(nvarchar(512),'+QUOTENAME(@SR_Key)+N'))))' END+
      N' FROM openalex.Faculty WITH(HOLDLOCK);';
    EXEC sys.sp_executesql @SR_Sql;
    SELECT @SR_NativeBefore=COUNT_BIG(*) FROM #SomRosterNative;

    IF OBJECT_ID(N'openalex.FacultyRoster',N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH(N'openalex.FacultyRoster',N'SourceRecordHash') IS NULL
           OR COL_LENGTH(N'openalex.FacultyRoster',N'NativeLinkStatus') IS NULL
            THROW 51417, 'An incompatible FacultyRoster table already exists.', 1;
        EXEC sys.sp_executesql N'INSERT #SomRosterExisting SELECT ComputingId,SourceRecordHash,NativeFacultyId,NativeLinkStatus FROM openalex.FacultyRoster WITH(UPDLOCK,HOLDLOCK);';
    END;
    IF EXISTS (SELECT 1 FROM #SomRosterInput i JOIN #SomRosterExisting e ON e.ComputingId=i.ComputingId
               WHERE i.Disposition=N'Import' AND (e.SourceRecordHash IS NULL OR e.SourceRecordHash<>i.SourceRecordHash))
        THROW 51418, 'An existing roster key has different source data. No rows were overwritten.', 1;

    INSERT #SomRosterCandidates(ComputingId,FacultyId)
    SELECT DISTINCT i.ComputingId,n.FacultyId FROM #SomRosterInput i CROSS JOIN #SomRosterNative n
    WHERE i.Disposition=N'Import' AND
      (n.SourceKey=i.ComputingId OR n.Email=i.ComputingId+N'@virginia.edu'
       OR n.SourceKey=i.ComputingId+N'@virginia.edu'
       OR (i.DirectoryEmail IS NOT NULL AND (n.Email=LOWER(i.DirectoryEmail) OR n.SourceKey=LOWER(i.DirectoryEmail))));

    INSERT #SomRosterPlan(ComputingId,AlreadyLoaded,NativeFacultyId,NativeLinkStatus)
    SELECT i.ComputingId,CASE WHEN e.ComputingId IS NULL THEN 0 ELSE 1 END,
      CASE WHEN e.ComputingId IS NOT NULL THEN e.NativeFacultyId
           WHEN a.CandidateCount=1 AND u.UserCount=1 AND NOT EXISTS
             (SELECT 1 FROM #SomRosterExisting other WHERE other.NativeFacultyId=a.FacultyId AND other.ComputingId<>i.ComputingId)
           THEN a.FacultyId ELSE NULL END,
      CASE WHEN e.ComputingId IS NOT NULL THEN e.NativeLinkStatus
           WHEN a.CandidateCount=1 AND u.UserCount=1 AND NOT EXISTS
             (SELECT 1 FROM #SomRosterExisting other WHERE other.NativeFacultyId=a.FacultyId AND other.ComputingId<>i.ComputingId)
           THEN N'ExactIdentifier'
           WHEN a.CandidateCount IS NOT NULL OR EXISTS
             (SELECT 1 FROM #SomRosterNative n WHERE LOWER(n.DisplayName)=LOWER(i.DisplayName)
              OR (NULLIF(i.FirstName,N'') IS NOT NULL AND NULLIF(i.LastName,N'') IS NOT NULL
                  AND LOWER(n.DisplayName) LIKE LOWER(i.FirstName)+N' %'+LOWER(i.LastName)))
           THEN N'NeedsReview' ELSE N'NotLinked' END
    FROM #SomRosterInput i
    LEFT JOIN #SomRosterExisting e ON e.ComputingId=i.ComputingId
    LEFT JOIN (SELECT ComputingId,COUNT(*) CandidateCount,MIN(FacultyId) FacultyId FROM #SomRosterCandidates GROUP BY ComputingId) a ON a.ComputingId=i.ComputingId
    LEFT JOIN (SELECT FacultyId,COUNT(*) UserCount FROM #SomRosterCandidates GROUP BY FacultyId) u ON u.FacultyId=a.FacultyId
    WHERE i.Disposition=N'Import';
    SELECT @SR_Inserted=COUNT(*) FROM #SomRosterPlan WHERE AlreadyLoaded=0;
    SELECT @SR_Reused=COUNT(*) FROM #SomRosterPlan WHERE AlreadyLoaded=1;

    SELECT N'Import plan' AS ReportSection,@SR_Run AS ImportRunId,@SR_Preview AS PreviewOnly,
      CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) AS ConnectedServer,DB_NAME() AS ConnectedDatabase,
      @SR_Expected AS SourceRecords,@SR_Accepted AS AcceptedFaculty,@SR_Quarantined AS QuarantinedRecords,
      @SR_Inserted AS WouldInsert,@SR_Reused AS AlreadyLoaded;
    SELECT N'Quarantine or identity review' AS ReportSection,i.SourceUserName,i.ComputingId,i.DisplayName,
      i.Disposition,i.ValidationNote,i.BaselineIdentityStatus,p.NativeFacultyId,p.NativeLinkStatus
    FROM #SomRosterInput i LEFT JOIN #SomRosterPlan p ON p.ComputingId=i.ComputingId
    WHERE i.Disposition=N'Quarantine' OR NULLIF(i.ValidationNote,N'') IS NOT NULL
       OR i.BaselineIdentityStatus LIKE N'Review%' OR p.NativeLinkStatus=N'NeedsReview'
    ORDER BY i.DisplayName;

    IF @SR_Preview=0
    BEGIN
        EXEC sys.sp_executesql N'
        IF OBJECT_ID(N''openalex.FacultyRosterImportRun'',N''U'') IS NULL
          CREATE TABLE openalex.FacultyRosterImportRun(
            ImportRunId uniqueidentifier NOT NULL PRIMARY KEY,ImportedAtUtc datetime2(7) NOT NULL,
            SourceServer nvarchar(128) NOT NULL,SourceDatabase nvarchar(128) NOT NULL,
            SourceExportRunId uniqueidentifier NOT NULL,SourceCapturedAtUtc datetime2(7) NOT NULL,
            IdentityEvidenceExportRunId uniqueidentifier NULL,PayloadHash varbinary(32) NOT NULL,
            SourceRecords int NOT NULL,AcceptedFaculty int NOT NULL,QuarantinedRecords int NOT NULL,
            InsertedRows int NOT NULL,ReusedRows int NOT NULL,NativeFacultyRowsBefore bigint NOT NULL,
            ScopeDescription nvarchar(256) NOT NULL);
        IF OBJECT_ID(N''openalex.FacultyRoster'',N''U'') IS NULL
          CREATE TABLE openalex.FacultyRoster(
            ComputingId nvarchar(32) NOT NULL PRIMARY KEY,SourceUserName nvarchar(255) NOT NULL,
            DisplayName nvarchar(512) NOT NULL,FirstName nvarchar(255) NULL,LastName nvarchar(255) NULL,
            PrimarySchoolCode nvarchar(20) NOT NULL,FacultyClassification nvarchar(50) NOT NULL,
            DepartmentCode nvarchar(100) NULL,OrganizationName nvarchar(512) NULL,AcademicRank nvarchar(255) NULL,
            AppointmentTitle nvarchar(512) NULL,RawDirectoryEmail nvarchar(512) NULL,DirectoryEmail nvarchar(320) NULL,
            DirectoryPersonId int NULL,DirectoryAccountStatus nvarchar(50) NULL,BaselineIdentityStatus nvarchar(40) NOT NULL,
            ValidationNote nvarchar(512) NULL,NativeFacultyId bigint NULL,NativeLinkStatus nvarchar(32) NOT NULL,
            SourceRecordHash varbinary(32) NOT NULL,SourceExportRunId uniqueidentifier NOT NULL,
            SourceCapturedAtUtc datetime2(7) NOT NULL,SourceFreshnessVerified bit NOT NULL,
            FirstImportRunId uniqueidentifier NOT NULL,CreatedAtUtc datetime2(7) NOT NULL);
        IF OBJECT_ID(N''openalex.FacultyRosterImportRow'',N''U'') IS NULL
          CREATE TABLE openalex.FacultyRosterImportRow(
            ImportRunId uniqueidentifier NOT NULL,SourceUserName nvarchar(255) NOT NULL,
            ComputingId nvarchar(32) NULL,DisplayName nvarchar(512) NOT NULL,Disposition nvarchar(16) NOT NULL,
            ValidationNote nvarchar(512) NULL,SourceRecordHash varbinary(32) NOT NULL,
            NativeFacultyId bigint NULL,NativeLinkStatus nvarchar(32) NULL,
            PRIMARY KEY(ImportRunId,SourceUserName),
            FOREIGN KEY(ImportRunId) REFERENCES openalex.FacultyRosterImportRun(ImportRunId));';

        IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id IN
          (OBJECT_ID(N'openalex.FacultyRoster'),OBJECT_ID(N'openalex.FacultyRosterImportRun'),OBJECT_ID(N'openalex.FacultyRosterImportRow')) AND is_disabled=0)
            THROW 51419, 'An import target has an enabled trigger; review it before applying.', 1;

        EXEC sys.sp_executesql N'
          INSERT openalex.FacultyRosterImportRun
            (ImportRunId,ImportedAtUtc,SourceServer,SourceDatabase,SourceExportRunId,SourceCapturedAtUtc,
             IdentityEvidenceExportRunId,PayloadHash,SourceRecords,AcceptedFaculty,QuarantinedRecords,
             InsertedRows,ReusedRows,NativeFacultyRowsBefore,ScopeDescription)
          SELECT @Run,@Now,c.SourceServer,c.SourceDatabase,c.SourceExportRunId,c.SourceCapturedAtUtc,
             c.IdentityEvidenceExportRunId,c.ExpectedPayloadHash,c.ExpectedCandidates,c.ExpectedAccepted,c.ExpectedQuarantined,
             @Inserted,@Reused,@NativeBefore,N''Working primary appointments: MBU=MD and FacultyType=Faculty; source freshness not verified''
          FROM #SomRosterContext c;
          INSERT openalex.FacultyRoster
            (ComputingId,SourceUserName,DisplayName,FirstName,LastName,PrimarySchoolCode,FacultyClassification,
             DepartmentCode,OrganizationName,AcademicRank,AppointmentTitle,RawDirectoryEmail,DirectoryEmail,
             DirectoryPersonId,DirectoryAccountStatus,BaselineIdentityStatus,ValidationNote,NativeFacultyId,NativeLinkStatus,
             SourceRecordHash,SourceExportRunId,SourceCapturedAtUtc,SourceFreshnessVerified,FirstImportRunId,CreatedAtUtc)
          SELECT i.ComputingId,i.SourceUserName,i.DisplayName,i.FirstName,i.LastName,i.PrimarySchoolCode,i.FacultyClassification,
             i.DepartmentCode,i.OrganizationName,i.AcademicRank,i.AppointmentTitle,i.RawDirectoryEmail,i.DirectoryEmail,
             i.DirectoryPersonId,i.DirectoryAccountStatus,i.BaselineIdentityStatus,i.ValidationNote,p.NativeFacultyId,p.NativeLinkStatus,
             i.SourceRecordHash,c.SourceExportRunId,c.SourceCapturedAtUtc,0,@Run,@Now
          FROM #SomRosterInput i JOIN #SomRosterPlan p ON p.ComputingId=i.ComputingId
          CROSS JOIN #SomRosterContext c WHERE i.Disposition=N''Import'' AND p.AlreadyLoaded=0;
          INSERT openalex.FacultyRosterImportRow
            (ImportRunId,SourceUserName,ComputingId,DisplayName,Disposition,ValidationNote,SourceRecordHash,NativeFacultyId,NativeLinkStatus)
          SELECT @Run,i.SourceUserName,i.ComputingId,i.DisplayName,i.Disposition,i.ValidationNote,i.SourceRecordHash,p.NativeFacultyId,p.NativeLinkStatus
          FROM #SomRosterInput i LEFT JOIN #SomRosterPlan p ON p.ComputingId=i.ComputingId;
          SELECT @Verified=COUNT(*) FROM #SomRosterInput i JOIN openalex.FacultyRoster r
            ON r.ComputingId=i.ComputingId AND r.SourceRecordHash=i.SourceRecordHash WHERE i.Disposition=N''Import'';
          IF (SELECT COUNT(*) FROM openalex.FacultyRosterImportRow WHERE ImportRunId=@Run)<>@Expected
            THROW 51420,''Import audit row count is incorrect.'',1;',
          N'@Run uniqueidentifier,@Now datetime2(7),@Inserted int,@Reused int,@NativeBefore bigint,@Expected int,@Verified int OUTPUT',
          @Run=@SR_Run,@Now=@SR_Now,@Inserted=@SR_Inserted,@Reused=@SR_Reused,
          @NativeBefore=@SR_NativeBefore,@Expected=@SR_Expected,@Verified=@SR_Verified OUTPUT;
        IF @SR_Verified<>@SR_Accepted THROW 51421, 'Post-import roster validation failed.', 1;
        SELECT @SR_NativeAfter=COUNT_BIG(*) FROM openalex.Faculty;
        IF @SR_NativeAfter<>@SR_NativeBefore THROW 51422, 'The native faculty count changed unexpectedly.', 1;
        COMMIT TRANSACTION;
        PRINT N'COMMITTED: accepted roster records were verified; quarantined source records were retained in the import audit.';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'PREVIEW ONLY: no permanent roster objects or records were written.';
    END;
    SELECT CASE WHEN @SR_Preview=1 THEN N'Preview only' ELSE N'Committed' END AS Outcome,
      @SR_Run AS ImportRunId,@SR_Expected AS SourceRecords,@SR_Accepted AS AcceptedFaculty,
      @SR_Quarantined AS QuarantinedRecords,@SR_Inserted AS NewRosterRows,@SR_Reused AS ReusedRosterRows,
      @SR_Verified AS VerifiedRosterRows,@SR_NativeBefore AS NativeFacultyRowsUnchanged;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    PRINT N'FAILED: the import transaction was rolled back. Earlier plan grids do not indicate a committed load.';
    THROW;
END CATCH;
