:On Error exit
/* Execute from repository root ONLY in a newly created, empty
   OpenAlexPrerequisiteValidation_<suffix> database. Real exported schema, not mocks. */
IF LEFT(DB_NAME(), LEN(N'OpenAlexPrerequisiteValidation_')) <> N'OpenAlexPrerequisiteValidation_'
    THROW 52400, 'Use a NEW OpenAlexPrerequisiteValidation_<suffix> scratch database.', 1;
GO
:r ./sql/baselines/Install_PATCH022_ReviewAuth_SQLCMD.sql
GO
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) <> 21
    THROW 52401, 'Unexpected scoped baseline table inventory.', 1;
IF COALESCE((SELECT SUM(p.rows) FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id
             WHERE t.is_ms_shipped = 0 AND p.index_id IN (0, 1)), 0) <> 0
    THROW 52401, 'Baseline installation unexpectedly seeded application data.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1)
   OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1)
    THROW 52401, 'The real baseline has untrusted/disabled constraints.', 1;
IF OBJECT_ID(N'academiconline.vw_FacultyPublicationReviewStatus', N'V') IS NULL
   OR OBJECT_ID(N'openalex.AppUserDepartmentAccessAudit', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AuthenticationEvent', N'U') IS NULL
   OR COL_LENGTH(N'openalex.AppUserRole', N'RevokedAtUtc') IS NULL
    THROW 52401, 'Required real auth/review objects or columns are missing.', 1;
PRINT N'PASS: real baseline installed, bound, constrained, and empty without seeded privileges';

DECLARE @SyncDefinition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment'));
DECLARE @SubmitDefinition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi'));
IF CHARINDEX(N'EmptyDepartmentReviewSync_NoCandidates_v1', @SyncDefinition) = 0
   OR CHARINDEX(N'ManualDoiReviewLifecycle_v1_BEGIN', @SyncDefinition) = 0
   OR CHARINDEX(N'LiveDoiSubmissionAuthorization_v1_BEGIN', @SubmitDefinition) = 0
   OR CHARINDEX(N'EnabledRoleStateAuthorization_v1', @SubmitDefinition) = 0
    THROW 52401, 'The ordered baseline installation lost a required guard.', 1;

-- Synthetic rows below are test fixtures only; nothing from the live DB is copied.
INSERT openalex.AppRole(RoleName, DisplayName) VALUES
    ('Admin', N'Synthetic Admin'), ('Reviewer', N'Synthetic Reviewer'), ('Reader', N'Synthetic Reader');
DECLARE @AdminRoleId bigint = (SELECT AppRoleId FROM openalex.AppRole WHERE RoleName = 'Admin');
DECLARE @ReviewerRoleId bigint = (SELECT AppRoleId FROM openalex.AppRole WHERE RoleName = 'Reviewer');
INSERT openalex.AppUser(UserName, DisplayName) VALUES (N'baseline.reviewer', N'Synthetic Reviewer');
DECLARE @ReviewerId bigint = CONVERT(bigint, SCOPE_IDENTITY());
INSERT openalex.AppUser(UserName, DisplayName) VALUES (N'baseline.admin', N'Synthetic Admin');
DECLARE @AdminId bigint = CONVERT(bigint, SCOPE_IDENTITY());
INSERT openalex.AppUserRole(AppUserId, AppRoleId) VALUES (@ReviewerId, @ReviewerRoleId), (@AdminId, @AdminRoleId);

INSERT academiconline.Department(SourceName, CanonicalName, LastRetrievedAtUtc)
VALUES (N'Synthetic Department', N'Synthetic Department', SYSUTCDATETIME());
DECLARE @DepartmentId bigint = CONVERT(bigint, SCOPE_IDENTITY());
INSERT academiconline.SyncRun
    (SyncType, DepartmentName, RunStatus, StartedBy, ApplicationVersion,
     ScholarsProcessed, PublicationsProcessed, ScholarsDiscovered, IsCacheComplete, SnapshotPromoted)
VALUES (N'Synthetic', N'Synthetic Department', N'Completed', N'baseline.fixture', N'PATCH022', 1, 0, 1, 1, 1);
DECLARE @RunId bigint = CONVERT(bigint, SCOPE_IDENTITY());
UPDATE academiconline.Department SET CurrentSnapshotRunId = @RunId WHERE DepartmentId = @DepartmentId;
INSERT academiconline.DepartmentRosterMember
    (DepartmentId, DisplayName, ExpectedEmail, SearchPriority, OpenAlexAuthorId, OpenAlexResolutionStatus)
VALUES (@DepartmentId, N'Synthetic Faculty', N'faculty1@virginia.edu', 0, N'A123', N'ProposedUnique');
DECLARE @RosterId bigint = CONVERT(bigint, SCOPE_IDENTITY());
DECLARE @FacultyKey nvarchar(600) = CONCAT(N'ROSTER:', @RosterId);
INSERT openalex.AppUserDepartmentAccess(AppUserId, DepartmentId) VALUES (@ReviewerId, @DepartmentId);
IF NOT EXISTS (SELECT 1 FROM academiconline.vw_FacultyPublicationSearch
               WHERE FacultyKey = @FacultyKey AND UvaUserId = N'faculty1'
                 AND DepartmentId = @DepartmentId AND DepartmentDataComplete = 1 AND DataRunId = @RunId)
    THROW 52401, 'Real faculty-search view did not resolve the synthetic current roster identity.', 1;

DECLARE @SyncOutcome TABLE
(
    DepartmentId bigint, SourceRows bigint, UpdatedRows int, InsertedRows int,
    RetiredRows int, CurrentCandidateRows bigint
);
INSERT @SyncOutcome EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment @DepartmentId;
IF NOT EXISTS (SELECT 1 FROM @SyncOutcome WHERE SourceRows = 0 AND CurrentCandidateRows = 0)
    THROW 52401, 'Real expected-empty snapshot failed NoCandidates validation.', 1;
PRINT N'PASS: real faculty view and expected-empty department synchronization';

DECLARE @Submission TABLE
(
    CandidateId bigint, PublicationKey bigint, WasCreated bit, SubmissionStatus nvarchar(40)
);
INSERT @Submission EXEC academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
    @FacultyKey = @FacultyKey, @DepartmentId = @DepartmentId, @UvaUserId = N'faculty1',
    @OpenAlexAuthorId = N'A123', @OpenAlexWorkId = N'W200',
    @DoiNormalized = N'10.1234/baseline-fixture', @Title = N'Synthetic baseline publication',
    @PublicationYear = 2023, @SubmittedBy = N'baseline.reviewer', @SubmittedByAppUserId = @ReviewerId;
DECLARE @CandidateId bigint = (SELECT CandidateId FROM @Submission);
IF @CandidateId IS NULL OR NOT EXISTS (SELECT 1 FROM @Submission WHERE WasCreated = 1)
    THROW 52401, 'Authorized submission into the real candidate schema failed.', 1;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment @DepartmentId;
IF NOT EXISTS (SELECT 1 FROM academiconline.vw_FacultyPublicationReviewStatus
               WHERE FacultyPublicationReviewCandidateId = @CandidateId
                 AND IsCurrentSource = 1 AND ReviewStatus = N'Pending' AND IsInCurrentReportingWindow = 0)
    THROW 52401, 'Real review-status view lost the manual Pending candidate after sync.', 1;
PRINT N'PASS: authorized submission stays Pending/current through real views and synchronization';

-- Each combination exercises the actual exported role/link columns with real FKs.
DECLARE @Case int = 1, @ActorId bigint, @ActorName nvarchar(256), @RoleId bigint;
WHILE @Case <= 6
BEGIN
    SET @ActorId = CASE WHEN @Case % 2 = 1 THEN @ReviewerId ELSE @AdminId END;
    SET @ActorName = CASE WHEN @Case % 2 = 1 THEN N'baseline.reviewer' ELSE N'baseline.admin' END;
    SET @RoleId = CASE WHEN @Case % 2 = 1 THEN @ReviewerRoleId ELSE @AdminRoleId END;
    UPDATE openalex.AppUserRole SET IsEnabled = 1, RevokedAtUtc = NULL;
    UPDATE openalex.AppRole SET IsEnabled = 1;
    IF @Case <= 2 UPDATE openalex.AppUserRole SET IsEnabled = 0 WHERE AppUserId = @ActorId;
    ELSE IF @Case <= 4 UPDATE openalex.AppRole SET IsEnabled = 0 WHERE AppRoleId = @RoleId;
    ELSE UPDATE openalex.AppUserRole SET RevokedAtUtc = SYSUTCDATETIME() WHERE AppUserId = @ActorId;
    DECLARE @AuditRows bigint = (SELECT COUNT_BIG(*) FROM academiconline.FacultyPublicationManualReviewSubmission);
    BEGIN TRY
        EXEC academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
            @FacultyKey = @FacultyKey, @DepartmentId = @DepartmentId, @UvaUserId = N'faculty1',
            @OpenAlexAuthorId = N'A123', @OpenAlexWorkId = N'W200',
            @DoiNormalized = N'10.1234/baseline-fixture', @Title = N'Synthetic baseline publication',
            @PublicationYear = 2023, @SubmittedBy = @ActorName, @SubmittedByAppUserId = @ActorId;
        THROW 52401, 'Inactive/revoked real role state unexpectedly authorized submission.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51403 THROW;
    END CATCH;
    IF @@TRANCOUNT <> 0 OR @AuditRows <> (SELECT COUNT_BIG(*) FROM academiconline.FacultyPublicationManualReviewSubmission)
        THROW 52401, 'Denied real-schema submission left writes or an open transaction.', 1;
    PRINT N'PASS: inactive/revoked real-schema role state case ' + CONVERT(nvarchar(10), @Case);
    SET @Case += 1;
END;
UPDATE openalex.AppUserRole SET IsEnabled = 1, RevokedAtUtc = NULL;
UPDATE openalex.AppRole SET IsEnabled = 1;

-- Decision table defaults, FK, uniqueness, status projection, and history are real.
EXEC academiconline.usp_SetFacultyPublicationReviewDecision
    @FacultyPublicationReviewCandidateId = @CandidateId, @DecisionStatus = N'Accepted', @ReviewedBy = N'baseline.reviewer';
EXEC academiconline.usp_SetFacultyPublicationReviewDecision
    @FacultyPublicationReviewCandidateId = @CandidateId, @DecisionStatus = N'Rejected', @ReviewedBy = N'baseline.reviewer';
IF (SELECT COUNT(*) FROM academiconline.FacultyPublicationReviewDecision WHERE FacultyPublicationReviewCandidateId = @CandidateId) <> 2
   OR (SELECT COUNT(*) FROM academiconline.FacultyPublicationReviewDecision WHERE FacultyPublicationReviewCandidateId = @CandidateId AND IsCurrent = 1) <> 1
   OR NOT EXISTS (SELECT 1 FROM academiconline.vw_FacultyPublicationReviewStatus
                  WHERE FacultyPublicationReviewCandidateId = @CandidateId AND ReviewStatus = N'Rejected')
    THROW 52401, 'Real review decision/history/status contract failed.', 1;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment @DepartmentId;
IF NOT EXISTS (SELECT 1 FROM academiconline.vw_FacultyPublicationReviewStatus
               WHERE FacultyPublicationReviewCandidateId = @CandidateId AND IsCurrentSource = 1 AND ReviewStatus = N'Rejected')
   OR EXISTS (SELECT 1 FROM academiconline.DepartmentRosterPublication)
   OR EXISTS (SELECT 1 FROM academiconline.DepartmentScholarPublication)
    THROW 52401, 'Real lifecycle synchronization changed review decisions or promoted source relationships.', 1;
PRINT N'PASS: real decision history, current decision uniqueness, and lifecycle preservation';
PRINT N'ALL REVIEW AUTH BASELINE CHECKS PASSED';
