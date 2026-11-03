:On Error exit
/* SQLCMD mode, -b required. Run sqlcmd from the repository root.
   Run ONLY in a newly created database named OpenAlexReviewSyncRegression
   or with the OpenAlexReviewSyncRegression_ prefix.
   The suite creates its own fixtures and refuses an existing application schema.
   It leaves the scratch database for inspection; it never drops a database. */
IF DB_NAME() <> N'OpenAlexReviewSyncRegression'
   AND LEFT(DB_NAME(), LEN(N'OpenAlexReviewSyncRegression_'))
       <> N'OpenAlexReviewSyncRegression_'
    THROW 52000, 'Use a fresh OpenAlexReviewSyncRegression scratch database.', 1;
IF SCHEMA_ID(N'academiconline') IS NOT NULL
    THROW 52000, 'Scratch schema already exists. Use a fresh scratch database.', 1;
EXEC(N'CREATE SCHEMA academiconline');
GO

CREATE TABLE academiconline.Department
(
    DepartmentId bigint PRIMARY KEY,
    CurrentSnapshotRunId bigint NULL,
    IsActive bit NOT NULL
);
CREATE TABLE academiconline.SyncRun
(
    AcademicOnlineSyncRunId bigint PRIMARY KEY,
    RunStatus nvarchar(30), IsCacheComplete bit, SnapshotPromoted bit,
    PublicationsProcessed int, ScholarsDiscovered int, ScholarsProcessed int,
    ScholarPaginationSafeguardHit bit, PublicationPaginationSafeguardHitCount int
);
CREATE TABLE academiconline.FixtureFaculty
(
    FacultyKey nvarchar(600), DepartmentId bigint,
    DepartmentDataComplete bit, DataRunId bigint
);
CREATE TABLE academiconline.DepartmentScholarPublication (DepartmentId bigint);
CREATE TABLE academiconline.DepartmentRosterPublication (DepartmentId bigint);
CREATE TABLE academiconline.FixtureReviewSource
(
    ReviewSourceKey nvarchar(1300), FacultyKey nvarchar(600), PublicationKey bigint,
    DepartmentId bigint, DepartmentName nvarchar(512), DivisionName nvarchar(512),
    UvaUserId nvarchar(512), FacultyDisplayName nvarchar(512), Email nvarchar(512),
    Position nvarchar(512), Orcid nvarchar(512), PublicationIdentitySource nvarchar(30),
    SourceAuthorId nvarchar(256), RelationshipSource nvarchar(60),
    RelationshipSourceProvider nvarchar(60), RelationshipSourceAuthorId nvarchar(256),
    AcademicOnlinePublicationId nvarchar(128), OpenAlexId nvarchar(128),
    Doi nvarchar(512), DoiNormalized nvarchar(512), Title nvarchar(max),
    Venue nvarchar(512), YearText nvarchar(50), PublicationYear int,
    CitationCount int, Verified bit, Link nvarchar(1024),
    DataRunId bigint, RelationshipSnapshotRunId bigint,
    IsInCurrentReportingWindow bit, DepartmentDataComplete bit
);
SELECT TOP (0) * INTO academiconline.FacultyPublicationReviewCandidate
FROM academiconline.FixtureReviewSource;
ALTER TABLE academiconline.FacultyPublicationReviewCandidate
    DROP COLUMN DataRunId, RelationshipSnapshotRunId, DepartmentDataComplete;
ALTER TABLE academiconline.FacultyPublicationReviewCandidate ADD
    FirstSeenDataRunId bigint, LastSeenDataRunId bigint,
    FirstSeenAtUtc datetime2, LastSeenAtUtc datetime2,
    IsCurrentSource bit, CreatedAtUtc datetime2, UpdatedAtUtc datetime2;
GO
CREATE VIEW academiconline.vw_FacultyPublicationSearch AS
    SELECT * FROM academiconline.FixtureFaculty;
GO
CREATE VIEW academiconline.vw_FacultyPublicationReviewUniverse AS
    SELECT * FROM academiconline.FixtureReviewSource;
GO
:r ./tests/sql/fixtures/ReviewSyncProcedure_BeforeFix.sql
GO
CREATE PROCEDURE academiconline._ResetEmptyReviewSyncFixture AS
BEGIN
    DELETE FROM academiconline.FacultyPublicationReviewCandidate;
    DELETE FROM academiconline.FixtureReviewSource;
    DELETE FROM academiconline.DepartmentScholarPublication;
    DELETE FROM academiconline.DepartmentRosterPublication;
    DELETE FROM academiconline.FixtureFaculty;
    DELETE FROM academiconline.SyncRun;
    DELETE FROM academiconline.Department;
    INSERT academiconline.Department VALUES (18, 353, 1);
    INSERT academiconline.SyncRun VALUES (353, N'Completed', 1, 1, 0, 1, 1, 0, 0);
    INSERT academiconline.FixtureFaculty VALUES (N'AO:123', 18, 1, 353);
END;
GO
CREATE PROCEDURE academiconline._ExpectEmptyReviewGuard @Case nvarchar(200) AS
BEGIN
    BEGIN TRY
        EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
        DECLARE @Failure nvarchar(2048) = N'Expected error 51101: ' + @Case;
        THROW 52001, @Failure, 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51101 THROW;
        PRINT N'PASS: guard retained: ' + @Case;
    END CATCH;
END;
GO
-- Reproduce the observed failure before installing the patch.
EXEC academiconline._ResetEmptyReviewSyncFixture;
EXEC academiconline._ExpectEmptyReviewGuard N'original procedure rejects expected empty snapshot';
GO
:r ./sql/450_EmptyDepartmentReviewSync_NoCandidates_SQL2016.sql
GO
-- Reapplication must be an explicit no-op, not a second guard insertion.
:r ./sql/450_EmptyDepartmentReviewSync_NoCandidates_SQL2016.sql
GO

DECLARE @Outcome TABLE
(
    DepartmentId bigint, SourceRows bigint, UpdatedRows int,
    InsertedRows int, RetiredRows int, CurrentCandidateRows bigint
);
EXEC academiconline._ResetEmptyReviewSyncFixture;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF (SELECT COUNT(*) FROM @Outcome) <> 1 OR EXISTS
    (SELECT 1 FROM @Outcome WHERE DepartmentId <> 18 OR SourceRows <> 0
     OR UpdatedRows <> 0 OR InsertedRows <> 0 OR RetiredRows <> 0 OR CurrentCandidateRows <> 0)
    THROW 52001, 'Expected the unchanged six-column result contract with zero counts.', 1;
IF EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate)
    THROW 52001, 'NoCandidates must not write candidates.', 1;
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF (SELECT COUNT(*) FROM @Outcome) <> 1 OR EXISTS
    (SELECT 1 FROM @Outcome WHERE InsertedRows <> 0 OR RetiredRows <> 0)
    THROW 52001, 'Repeating NoCandidates must remain a no-op.', 1;
PRINT N'PASS: expected empty snapshot returns zero counts repeatedly';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET IsCacheComplete = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'incomplete snapshot';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET SnapshotPromoted = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'unpromoted snapshot';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.Department SET CurrentSnapshotRunId = NULL;
EXEC academiconline._ExpectEmptyReviewGuard N'missing current snapshot';

EXEC academiconline._ResetEmptyReviewSyncFixture;
DELETE academiconline.SyncRun;
EXEC academiconline._ExpectEmptyReviewGuard N'missing run metadata';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET RunStatus = N'Failed';
EXEC academiconline._ExpectEmptyReviewGuard N'failed run';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET PublicationsProcessed = 1;
EXEC academiconline._ExpectEmptyReviewGuard N'publications processed but no source associations';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET ScholarsProcessed = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'partially processed faculty';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET ScholarPaginationSafeguardHit = 1;
EXEC academiconline._ExpectEmptyReviewGuard N'scholar pagination safeguard';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.SyncRun SET PublicationPaginationSafeguardHitCount = 1;
EXEC academiconline._ExpectEmptyReviewGuard N'publication pagination safeguard';

EXEC academiconline._ResetEmptyReviewSyncFixture;
DELETE academiconline.FixtureFaculty;
EXEC academiconline._ExpectEmptyReviewGuard N'faculty view unexpectedly empty';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.FixtureFaculty SET DepartmentDataComplete = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'faculty source incomplete';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.FixtureFaculty SET DataRunId = 352;
EXEC academiconline._ExpectEmptyReviewGuard N'faculty source from another snapshot';

EXEC academiconline._ResetEmptyReviewSyncFixture;
UPDATE academiconline.FixtureFaculty SET FacultyKey = N'UNKNOWN:123';
EXEC academiconline._ExpectEmptyReviewGuard N'unsupported faculty identity key';

EXEC academiconline._ResetEmptyReviewSyncFixture;
INSERT academiconline.FacultyPublicationReviewCandidate
    (DepartmentId, FacultyKey, PublicationKey, IsCurrentSource, IsInCurrentReportingWindow)
VALUES (18, N'AO:123', 100, 1, 1);
EXEC academiconline._ExpectEmptyReviewGuard N'existing current candidates';
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE DepartmentId = 18 AND IsCurrentSource = 1 AND IsInCurrentReportingWindow = 1)
    THROW 52001, 'Existing candidate changed after rejecting empty source.', 1;
UPDATE academiconline.FacultyPublicationReviewCandidate SET IsCurrentSource = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'existing historical candidates';

EXEC academiconline._ResetEmptyReviewSyncFixture;
INSERT academiconline.DepartmentScholarPublication VALUES (18);
EXEC academiconline._ExpectEmptyReviewGuard N'known AO links missing from review source';

EXEC academiconline._ResetEmptyReviewSyncFixture;
INSERT academiconline.DepartmentRosterPublication VALUES (18);
EXEC academiconline._ExpectEmptyReviewGuard N'known roster links missing from review source';

-- Another department's candidate must neither block this no-op nor change.
EXEC academiconline._ResetEmptyReviewSyncFixture;
INSERT academiconline.FacultyPublicationReviewCandidate
    (DepartmentId, FacultyKey, PublicationKey, IsCurrentSource)
VALUES (19, N'AO:456', 200, 1);
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE DepartmentId = 19 AND IsCurrentSource = 1)
    THROW 52001, 'Another department candidate changed.', 1;
PRINT N'PASS: another department is unaffected';

-- Original nonempty insert, update, and retire behavior must remain intact.
EXEC academiconline._ResetEmptyReviewSyncFixture;
INSERT academiconline.FixtureReviewSource
    (DepartmentId, FacultyKey, PublicationKey, ReviewSourceKey, Title,
     DataRunId, IsInCurrentReportingWindow)
VALUES (18, N'AO:123', 100, N'AO:123|100', N'First title', 353, 1);
INSERT academiconline.FacultyPublicationReviewCandidate
    (DepartmentId, FacultyKey, PublicationKey, IsCurrentSource)
VALUES (18, N'AO:123', 999, 1);
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE SourceRows = 1 AND InsertedRows = 1
               AND RetiredRows = 1 AND CurrentCandidateRows = 1)
    THROW 52001, 'Nonempty insert/retire behavior changed.', 1;
UPDATE academiconline.FixtureReviewSource SET Title = N'Updated title';
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE UpdatedRows = 1 AND InsertedRows = 0 AND RetiredRows = 0)
   OR NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
                  WHERE PublicationKey = 100 AND Title = N'Updated title')
    THROW 52001, 'Nonempty update behavior changed.', 1;
PRINT N'PASS: nonempty insert, update, retirement, and result counts unchanged';

INSERT academiconline.FixtureReviewSource
    (DepartmentId, FacultyKey, PublicationKey) VALUES (18, N'AO:123', 100);
BEGIN TRY
    EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
    THROW 52001, 'Expected duplicate-source error 51102.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 51102 THROW;
    PRINT N'PASS: duplicate-source guard 51102 retained';
END CATCH;

GO
:r ./sql/rollback/450_EmptyDepartmentReviewSync_NoCandidates_Rollback_SQL2016.sql
GO
EXEC academiconline._ResetEmptyReviewSyncFixture;
EXEC academiconline._ExpectEmptyReviewGuard N'rollback restores original behavior';
GO
:r ./sql/450_EmptyDepartmentReviewSync_NoCandidates_SQL2016.sql
GO
DECLARE @Reapplied TABLE
(
    DepartmentId bigint, SourceRows bigint, UpdatedRows int,
    InsertedRows int, RetiredRows int, CurrentCandidateRows bigint
);
INSERT @Reapplied EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Reapplied WHERE SourceRows = 0 AND CurrentCandidateRows = 0)
    THROW 52001, 'Reapplying after rollback did not restore NoCandidates behavior.', 1;
PRINT N'PASS: rollback and reapplication';
PRINT N'ALL REGRESSION CHECKS PASSED';
