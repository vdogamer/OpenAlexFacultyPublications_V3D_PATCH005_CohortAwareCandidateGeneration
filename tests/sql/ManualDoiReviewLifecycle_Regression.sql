:On Error exit
/* Run from the repository root in a NEW OpenAlexReviewSyncRegression_<suffix> DB.
   The included baseline suite verifies isolation and creates synthetic fixtures.
   Only the final MANUAL DOI success message denotes completion of this suite. */
:r ./tests/sql/EmptyDepartmentReviewSync_Regression.sql
GO

-- Extend only the scratch fixtures to execute the actual PATCH022C submission.
ALTER TABLE academiconline.FacultyPublicationReviewCandidate ADD
    FacultyPublicationReviewCandidateId bigint IDENTITY(1,1) NOT NULL;
CREATE UNIQUE INDEX UX_ManualFixture_CandidatePair
    ON academiconline.FacultyPublicationReviewCandidate(FacultyKey, PublicationKey);
ALTER TABLE academiconline.FixtureFaculty ADD
    DepartmentName nvarchar(512), DivisionName nvarchar(512), UvaUserId nvarchar(512),
    DisplayName nvarchar(512), Email nvarchar(512), Position nvarchar(512),
    Orcid nvarchar(512), PublicationIdentitySource nvarchar(30),
    SourceAuthorId nvarchar(256), AcademicOnlineAuthorId nvarchar(256);
EXEC sys.sp_refreshview N'academiconline.vw_FacultyPublicationSearch';

CREATE TABLE academiconline.Publication
(
    PublicationKey bigint IDENTITY(1,1) PRIMARY KEY,
    AcademicOnlinePublicationId nvarchar(128), OpenAlexId nvarchar(128),
    Doi nvarchar(512), DoiNormalized nvarchar(512), Title nvarchar(max),
    TitleYearFingerprint nvarchar(1024), Venue nvarchar(512), YearText nvarchar(50),
    PublicationYear int, CitationCount int, Verified bit, Link nvarchar(1024),
    CoauthorsJson nvarchar(max), KeywordsJson nvarchar(max), RawJson nvarchar(max),
    LastRetrievedAtUtc datetime2, LastSyncRunId bigint
);
CREATE TABLE academiconline.DepartmentRosterMember
(
    DepartmentRosterMemberId bigint PRIMARY KEY, DepartmentId bigint,
    IsActive bit, OpenAlexAuthorId nvarchar(256),
    OpenAlexResolutionStatus nvarchar(60), ExpectedEmail nvarchar(512),
    AcademicOnlineAuthorId nvarchar(256)
);
GO
:r ./tests/sql/fixtures/ManualDoiSubmission_PATCH022C.sql
GO

CREATE PROCEDURE academiconline._ResetManualDoiFixture AS
BEGIN
    DELETE FROM academiconline.FacultyPublicationManualReviewSubmission;
    DELETE FROM academiconline.FacultyPublicationReviewCandidate;
    DELETE FROM academiconline.FixtureReviewSource;
    DELETE FROM academiconline.DepartmentScholarPublication;
    DELETE FROM academiconline.DepartmentRosterPublication;
    DELETE FROM academiconline.FixtureFaculty;
    DELETE FROM academiconline.SyncRun;
    DELETE FROM academiconline.Department;
    DELETE FROM academiconline.DepartmentRosterMember;
    DELETE FROM academiconline.Publication;
    INSERT academiconline.Department VALUES (18, 353, 1);
    INSERT academiconline.SyncRun VALUES (353, N'Completed', 1, 1, 0, 1, 1, 0, 0);
    INSERT academiconline.FixtureFaculty
        (FacultyKey, DepartmentId, DepartmentDataComplete, DataRunId,
         DepartmentName, UvaUserId, DisplayName, Email,
         PublicationIdentitySource, SourceAuthorId)
    VALUES (N'ROSTER:1', 18, 1, 353, N'Fixture Department', N'fixture1',
            N'Fixture Faculty', N'fixture1@example.org', N'OpenAlex', N'A123');
    INSERT academiconline.DepartmentRosterMember
        (DepartmentRosterMemberId, DepartmentId, IsActive, OpenAlexAuthorId,
         OpenAlexResolutionStatus, ExpectedEmail)
    VALUES (1, 18, 1, N'A123', N'ProposedUnique', N'fixture1@example.org');
END;
GO
CREATE PROCEDURE academiconline._SubmitManualDoiFixture AS
BEGIN
    EXEC academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
        @FacultyKey = N'ROSTER:1', @DepartmentId = 18, @UvaUserId = N'fixture1',
        @OpenAlexAuthorId = N'A123', @OpenAlexWorkId = N'W200',
        @DoiNormalized = N'10.1234/manual-fixture', @Title = N'Manual DOI fixture',
        @PublicationYear = 2023, @SubmittedBy = N'fixture.reviewer';
END;
GO
CREATE PROCEDURE academiconline._SeedNativeReviewFixture
    @PublicationKey bigint OUTPUT, @FacultyKey nvarchar(600) = N'ROSTER:1'
AS
BEGIN
    INSERT academiconline.Publication(OpenAlexId, Title, PublicationYear)
    VALUES (N'W300', N'Native publication fixture', 2026);
    SET @PublicationKey = CONVERT(bigint, SCOPE_IDENTITY());
    INSERT academiconline.FixtureReviewSource
        (ReviewSourceKey, FacultyKey, PublicationKey, DepartmentId,
         DepartmentName, FacultyDisplayName, PublicationIdentitySource,
         SourceAuthorId, RelationshipSource, RelationshipSourceProvider,
         RelationshipSourceAuthorId, Title, DataRunId,
         IsInCurrentReportingWindow, DepartmentDataComplete)
    VALUES (CONCAT(@FacultyKey, N'|', @PublicationKey), @FacultyKey, @PublicationKey, 18,
            N'Fixture Department', N'Fixture Faculty', N'OpenAlex', N'A123',
            N'Roster', N'OpenAlex', N'A123', N'Native publication fixture', 353, 1, 1);
END;
GO

-- Reproduce the blocker with the actual submission procedure before applying 457.
DECLARE @Submission TABLE
(
    CandidateId bigint, PublicationKey bigint, WasCreated bit, SubmissionStatus nvarchar(40)
);
DECLARE @NativeKey bigint;
EXEC academiconline._ResetManualDoiFixture;
INSERT @Submission EXEC academiconline._SubmitManualDoiFixture;
EXEC academiconline._SeedNativeReviewFixture @PublicationKey = @NativeKey OUTPUT;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS
    (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate c
     JOIN @Submission s ON s.CandidateId = c.FacultyPublicationReviewCandidateId
     WHERE c.IsCurrentSource = 0)
    THROW 52101, 'The baseline did not reproduce retirement of the manual candidate.', 1;
PRINT N'REPRODUCED: normal sync retires the PATCH022C manual candidate before 457';
GO
:r ./sql/457_PATCH022F_ManualDoiReviewLifecycle_SQL2016.sql
GO
:r ./sql/457_PATCH022F_ManualDoiReviewLifecycle_SQL2016.sql
GO

DECLARE @Outcome TABLE
(
    DepartmentId bigint, SourceRows bigint, UpdatedRows int,
    InsertedRows int, RetiredRows int, CurrentCandidateRows bigint
);
DECLARE @Submission TABLE
(
    CandidateId bigint, PublicationKey bigint, WasCreated bit, SubmissionStatus nvarchar(40)
);
DECLARE @ManualId bigint, @ManualKey bigint, @NativeKey bigint, @OtherKey bigint;
SELECT TOP (1)
    @ManualId = FacultyPublicationReviewCandidateId, @ManualKey = PublicationKey
FROM academiconline.FacultyPublicationManualReviewSubmission
WHERE SubmissionStatus = N'CreatedPendingCandidate';

-- Installing the DDL alone must not repair or mutate review data.
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE FacultyPublicationReviewCandidateId = @ManualId AND IsCurrentSource = 0)
    THROW 52101, 'Installation unexpectedly modified review candidates.', 1;

-- A normal reconciliation restores the same row and leaves automatic publication
-- relationships untouched. A manual association is not a promoted source row.
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE FacultyPublicationReviewCandidateId = @ManualId
                 AND PublicationKey = @ManualKey AND IsCurrentSource = 1
                 AND IsInCurrentReportingWindow = 0 AND RelationshipSource = N'ManualDoiLookup')
    THROW 52101, 'Previously retired manual candidate was not restored in place.', 1;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE SourceRows = 2 AND RetiredRows = 0)
    THROW 52101, 'Expected native plus manual source rows without retirement.', 1;
IF EXISTS (SELECT 1 FROM academiconline.DepartmentScholarPublication)
   OR EXISTS (SELECT 1 FROM academiconline.DepartmentRosterPublication)
    THROW 52101, 'Manual review sync must not create source publication relationships.', 1;
PRINT N'PASS: native plus manual sync restores the original candidate without auto-publishing';

DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
INSERT @Submission EXEC academiconline._SubmitManualDoiFixture;
IF NOT EXISTS (SELECT 1 FROM @Submission WHERE CandidateId = @ManualId AND WasCreated = 0
               AND SubmissionStatus = N'AlreadyExists')
   OR (SELECT COUNT(*) FROM academiconline.FacultyPublicationReviewCandidate
       WHERE FacultyKey = N'ROSTER:1' AND PublicationKey = @ManualKey) <> 1
    THROW 52101, 'Resubmission or repeated sync duplicated the manual candidate.', 1;
PRINT N'PASS: repeat synchronization and resubmission retain one current candidate';

-- Repeated current faculty metadata must not multiply the manual association.
INSERT academiconline.FixtureFaculty SELECT * FROM academiconline.FixtureFaculty;
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE SourceRows = 2 AND RetiredRows = 0)
    THROW 52101, 'Repeated faculty metadata duplicated the manual source.', 1;
;WITH Duplicates AS
(
    SELECT ROW_NUMBER() OVER (PARTITION BY FacultyKey, DepartmentId ORDER BY DataRunId) AS RowNumber
    FROM academiconline.FixtureFaculty
)
DELETE FROM Duplicates WHERE RowNumber > 1;
PRINT N'PASS: repeated faculty metadata does not duplicate manual candidates';

-- If the automatic source later contains this pair, use its metadata once, not
-- a duplicate manual row. Preserve manual provenance in the durable audit.
INSERT academiconline.FixtureReviewSource
    (ReviewSourceKey, FacultyKey, PublicationKey, DepartmentId, DepartmentName,
     RelationshipSource, RelationshipSourceProvider, DataRunId,
     IsInCurrentReportingWindow, DepartmentDataComplete)
VALUES (N'ObservedManualPair', N'ROSTER:1', @ManualKey, 18, N'Fixture Department',
        N'Roster', N'OpenAlex', 353, 1, 1);
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE SourceRows = 2 AND RetiredRows = 0)
   OR NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
                  WHERE FacultyPublicationReviewCandidateId = @ManualId
                    AND RelationshipSource = N'Roster' AND IsInCurrentReportingWindow = 1)
    THROW 52101, 'Automatic source handoff duplicated or lost the manual-origin pair.', 1;
DELETE academiconline.FixtureReviewSource WHERE PublicationKey = @ManualKey;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE FacultyPublicationReviewCandidateId = @ManualId
                 AND IsCurrentSource = 1 AND RelationshipSource = N'ManualDoiLookup')
    THROW 52101, 'Manual origin was lost after automatic source metadata overwrote the row.', 1;
PRINT N'PASS: manual association survives automatic-source appearance and disappearance';

-- An AlreadyExists audit alone must not preserve a genuinely automatic row.
EXEC academiconline._SeedNativeReviewFixture @PublicationKey = @OtherKey OUTPUT;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
INSERT academiconline.FacultyPublicationManualReviewSubmission
    (FacultyPublicationReviewCandidateId, PublicationKey, FacultyKey, DepartmentId,
     UvaUserId, DoiNormalized, OpenAlexAuthorId, SubmissionStatus, SubmittedBy)
SELECT FacultyPublicationReviewCandidateId, PublicationKey, FacultyKey, DepartmentId,
       N'fixture1', N'10.1234/automatic-fixture', N'A123', N'AlreadyExists', N'fixture.reviewer'
FROM academiconline.FacultyPublicationReviewCandidate WHERE PublicationKey = @OtherKey;
DELETE academiconline.FixtureReviewSource WHERE PublicationKey = @OtherKey;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE PublicationKey = @OtherKey AND IsCurrentSource = 0)
    THROW 52101, 'An AlreadyExists audit improperly protected an automatic candidate.', 1;
PRINT N'PASS: ordinary source-managed retirement is unchanged';

-- Manual-only, expected-empty snapshot: preserve the row rather than raising
-- 51101 or weakening the safeguards for unrelated automatic candidates.
EXEC academiconline._ResetManualDoiFixture;
DELETE @Submission;
INSERT @Submission EXEC academiconline._SubmitManualDoiFixture;
SELECT @ManualId = CandidateId, @ManualKey = PublicationKey FROM @Submission;
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE SourceRows = 1 AND CurrentCandidateRows = 1
               AND InsertedRows = 0 AND RetiredRows = 0)
    THROW 52101, 'Expected-empty automatic source did not preserve the manual-only candidate.', 1;
UPDATE academiconline.FacultyPublicationReviewCandidate
SET IsCurrentSource = 0, IsInCurrentReportingWindow = 0;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE FacultyPublicationReviewCandidateId = @ManualId AND IsCurrentSource = 1)
    THROW 52101, 'Manual-only reconciliation failed to restore a retired candidate.', 1;
PRINT N'PASS: manual-only snapshot and in-place recovery';

UPDATE academiconline.SyncRun SET PublicationsProcessed = 1;
EXEC academiconline._ExpectEmptyReviewGuard N'manual row cannot mask missing processed publications';
UPDATE academiconline.SyncRun SET PublicationsProcessed = 0, IsCacheComplete = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'manual row cannot mask incomplete snapshot';
UPDATE academiconline.SyncRun SET IsCacheComplete = 1, SnapshotPromoted = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'manual row cannot mask unpromoted snapshot';
UPDATE academiconline.SyncRun SET SnapshotPromoted = 1;
INSERT academiconline.DepartmentScholarPublication VALUES (18);
EXEC academiconline._ExpectEmptyReviewGuard N'manual row cannot hide missing AO source relationships';
DELETE academiconline.DepartmentScholarPublication;
INSERT academiconline.DepartmentRosterPublication VALUES (18);
EXEC academiconline._ExpectEmptyReviewGuard N'manual row cannot hide missing roster source relationships';
DELETE academiconline.DepartmentRosterPublication;
INSERT academiconline.FacultyPublicationReviewCandidate
    (FacultyKey, PublicationKey, DepartmentId, IsCurrentSource)
VALUES (N'ROSTER:1', 999999, 18, 1);
EXEC academiconline._ExpectEmptyReviewGuard N'manual row cannot hide an absent automatic candidate';
DELETE academiconline.FacultyPublicationReviewCandidate WHERE PublicationKey = 999999;

-- Provenance must match candidate ID, department, faculty, publication, and the
-- original creation status. A mutable label on the candidate is insufficient.
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET FacultyPublicationReviewCandidateId = -1;
EXEC academiconline._ExpectEmptyReviewGuard N'audit for another candidate does not establish provenance';
UPDATE academiconline.FacultyPublicationManualReviewSubmission
SET FacultyPublicationReviewCandidateId = @ManualId, PublicationKey = -1;
EXEC academiconline._ExpectEmptyReviewGuard N'audit for another publication does not establish provenance';
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET PublicationKey = @ManualKey, OpenAlexAuthorId = NULL;
EXEC academiconline._ExpectEmptyReviewGuard N'creation audit must identify the submitted author';
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET OpenAlexAuthorId = N'A123';
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET DepartmentId = 19;
EXEC academiconline._ExpectEmptyReviewGuard N'audit from another department does not establish provenance';
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET DepartmentId = 18, FacultyKey = N'ROSTER:999';
EXEC academiconline._ExpectEmptyReviewGuard N'audit from another faculty does not establish provenance';
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET FacultyKey = N'ROSTER:1', SubmissionStatus = N'AlreadyExists';
EXEC academiconline._ExpectEmptyReviewGuard N'AlreadyExists is not a creation audit';
UPDATE academiconline.FacultyPublicationManualReviewSubmission SET SubmissionStatus = N'CreatedPendingCandidate';
UPDATE academiconline.FixtureFaculty SET DepartmentDataComplete = 0;
EXEC academiconline._ExpectEmptyReviewGuard N'incomplete faculty identity is not eligible';
UPDATE academiconline.FixtureFaculty SET DepartmentDataComplete = 1, DataRunId = 352;
EXEC academiconline._ExpectEmptyReviewGuard N'stale faculty identity is not eligible';
UPDATE academiconline.FixtureFaculty SET DataRunId = 353;

-- No manual row: retain the original all-zero NoCandidates result.
EXEC academiconline._ResetManualDoiFixture;
DELETE @Outcome;
INSERT @Outcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM @Outcome WHERE SourceRows = 0 AND CurrentCandidateRows = 0
               AND UpdatedRows = 0 AND InsertedRows = 0 AND RetiredRows = 0)
    THROW 52101, 'Original NoCandidates outcome changed.', 1;
PRINT N'PASS: original NoCandidates outcome retained';

-- Another department must not be modified.
INSERT @Submission EXEC academiconline._SubmitManualDoiFixture;
INSERT academiconline.FacultyPublicationReviewCandidate
    (FacultyKey, PublicationKey, DepartmentId, IsCurrentSource)
VALUES (N'AO:other', 999998, 19, 1);
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE DepartmentId = 19 AND IsCurrentSource = 1)
    THROW 52101, 'Another department was modified.', 1;
PRINT N'PASS: department isolation';

-- Faculty who are no longer eligible are not silently kept active. With another
-- valid automatic source present, normal retirement still applies to that row.
EXEC academiconline._ResetManualDoiFixture;
DELETE @Submission;
INSERT @Submission EXEC academiconline._SubmitManualDoiFixture;
SELECT @ManualId = CandidateId FROM @Submission;
EXEC academiconline._SeedNativeReviewFixture
    @PublicationKey = @NativeKey OUTPUT, @FacultyKey = N'AO:other';
DELETE academiconline.FixtureFaculty;
INSERT academiconline.FixtureFaculty
    (FacultyKey, DepartmentId, DepartmentDataComplete, DataRunId, DisplayName)
VALUES (N'AO:other', 18, 1, 353, N'Another current faculty');
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE FacultyPublicationReviewCandidateId = @ManualId AND IsCurrentSource = 0)
    THROW 52101, 'Manual preservation ignored loss of current faculty eligibility.', 1;
PRINT N'PASS: manual preservation requires current faculty eligibility';
GO

:r ./sql/rollback/457_PATCH022F_ManualDoiReviewLifecycle_Rollback_SQL2016.sql
GO
-- Rollback retains 450 but restores the original manual retirement behavior.
EXEC academiconline._ResetManualDoiFixture;
DECLARE @Submission TABLE
(
    CandidateId bigint, PublicationKey bigint, WasCreated bit, SubmissionStatus nvarchar(40)
);
DECLARE @NativeKey bigint;
INSERT @Submission EXEC academiconline._SubmitManualDoiFixture;
EXEC academiconline._SeedNativeReviewFixture @PublicationKey = @NativeKey OUTPUT;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate c
               JOIN @Submission s ON s.CandidateId = c.FacultyPublicationReviewCandidateId
               WHERE c.IsCurrentSource = 0)
    THROW 52101, 'Rollback did not restore the baseline manual retirement behavior.', 1;
IF CHARINDEX(N'/* EmptyDepartmentReviewSync_NoCandidates_v1 */', OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment'))) = 0
    THROW 52101, 'Rollback removed the separate NoCandidates fix.', 1;
PRINT N'PASS: lifecycle rollback retains the original NoCandidates fix';
GO
:r ./sql/457_PATCH022F_ManualDoiReviewLifecycle_SQL2016.sql
GO
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS
    (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
     WHERE RelationshipSource = N'ManualDoiLookup' AND IsCurrentSource = 1)
    THROW 52101, 'Reapplying 457 did not restore the manual candidate.', 1;
PRINT N'ALL MANUAL DOI LIFECYCLE CHECKS PASSED';
