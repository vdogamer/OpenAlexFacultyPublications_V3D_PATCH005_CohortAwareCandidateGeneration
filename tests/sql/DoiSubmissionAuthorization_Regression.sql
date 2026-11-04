:On Error exit
/* Run ONLY from the repository root against a NEW OpenAlexReviewSyncRegression_<suffix>
   scratch DB. Includes the existing isolation guard and all 450/457 regressions.
   Only the final DOI SUBMISSION AUTHORIZATION message denotes full completion. */
:r ./tests/sql/ManualDoiReviewLifecycle_Regression.sql
GO
IF SCHEMA_ID(N'openalex') IS NOT NULL
    THROW 52200, 'Unexpected auth schema in scratch database; stop and inspect.', 1;
EXEC(N'CREATE SCHEMA openalex');
GO
CREATE TABLE openalex.AppUser
(
    AppUserId bigint PRIMARY KEY, UserName nvarchar(256) UNIQUE, IsEnabled bit NOT NULL
);
CREATE TABLE openalex.AppRole (AppRoleId bigint PRIMARY KEY, RoleName nvarchar(100));
CREATE TABLE openalex.AppUserRole
(
    AppUserId bigint, AppRoleId bigint, PRIMARY KEY(AppUserId, AppRoleId)
);
CREATE TABLE openalex.AppUserDepartmentAccess
(
    AppUserId bigint, DepartmentId bigint, IsActive bit, CanReview bit,
    PRIMARY KEY(AppUserId, DepartmentId)
);
CREATE TABLE academiconline.FixtureSubmissionObjectId (OriginalObjectId int);
INSERT academiconline.FixtureSubmissionObjectId
SELECT OBJECT_ID(N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi');
CREATE ROLE DoiAuthorizationFixtureExecutor;
GRANT EXECUTE ON OBJECT::academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
    TO DoiAuthorizationFixtureExecutor;

-- Reproduce the original SQL boundary accepting a disabled submitter.
EXEC academiconline._ResetManualDoiFixture;
INSERT openalex.AppUser VALUES (12, N'fixture.reviewer', 0);
EXEC academiconline._SubmitManualDoiFixture;
IF (SELECT COUNT(*) FROM academiconline.FacultyPublicationManualReviewSubmission) <> 1
    THROW 52201, 'The original disabled-submitter SQL boundary was not reproduced.', 1;
PRINT N'REPRODUCED: original submission procedure accepts a disabled submitter';
GO
:r ./sql/458_PATCH022G_DoiSubmissionLiveAuthorization_SQL2016.sql
GO
:r ./sql/458_PATCH022G_DoiSubmissionLiveAuthorization_SQL2016.sql
GO
IF NOT EXISTS (SELECT 1 FROM academiconline.FixtureSubmissionObjectId
    WHERE OriginalObjectId = OBJECT_ID(N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi'))
    THROW 52201, 'Authorization installer replaced the procedure object.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_permissions
    WHERE major_id = OBJECT_ID(N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi')
      AND grantee_principal_id = DATABASE_PRINCIPAL_ID(N'DoiAuthorizationFixtureExecutor')
      AND permission_name = N'EXECUTE' AND state = N'G')
    THROW 52201, 'Authorization installer lost the existing EXECUTE grant.', 1;
IF (SELECT COUNT(*) FROM academiconline.FacultyPublicationManualReviewSubmission) <> 1
    THROW 52201, 'Installing authorization unexpectedly changed submission data.', 1;
PRINT N'PASS: DDL-only installation, idempotency, procedure object, and grant preservation';
GO
CREATE PROCEDURE academiconline._ResetDoiAuthorizationFixture AS
BEGIN
    EXEC academiconline._ResetManualDoiFixture;
    DELETE openalex.AppUserDepartmentAccess;
    DELETE openalex.AppUserRole;
    DELETE openalex.AppRole;
    DELETE openalex.AppUser;
    INSERT openalex.AppUser VALUES
        (12, N'fixture.reviewer', 1), (13, N'fixture.admin', 1), (14, N'fixture.reader', 1);
    INSERT openalex.AppRole VALUES (1, N'Admin'), (2, N'Reviewer'), (3, N'Reader');
    INSERT openalex.AppUserRole VALUES (12, 2), (13, 1), (14, 3);
    INSERT openalex.AppUserDepartmentAccess VALUES (12, 18, 1, 1), (14, 18, 1, 1);
END;
GO
CREATE PROCEDURE academiconline._SubmitAuthorizedDoiFixture
    @ActorId bigint = 12, @ActorName nvarchar(256) = N'fixture.reviewer'
AS
BEGIN
    EXEC academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
        @FacultyKey = N'ROSTER:1', @DepartmentId = 18, @UvaUserId = N'fixture1',
        @OpenAlexAuthorId = N'A123', @OpenAlexWorkId = N'W200',
        @DoiNormalized = N'10.1234/manual-fixture', @Title = N'Manual DOI fixture',
        @PublicationYear = 2023, @SubmittedBy = @ActorName,
        @SubmittedByAppUserId = @ActorId;
END;
GO
CREATE PROCEDURE academiconline._ExpectDoiAuthorizationDenial
    @Case nvarchar(200), @ActorId bigint = 12, @ActorName nvarchar(256) = N'fixture.reviewer'
AS
BEGIN
    DECLARE @Publications bigint = (SELECT COUNT_BIG(*) FROM academiconline.Publication);
    DECLARE @Candidates bigint = (SELECT COUNT_BIG(*) FROM academiconline.FacultyPublicationReviewCandidate);
    DECLARE @Audits bigint = (SELECT COUNT_BIG(*) FROM academiconline.FacultyPublicationManualReviewSubmission);
    DECLARE @PublicationState TABLE (PublicationKey bigint, Title nvarchar(max), LastRetrievedAtUtc datetime2);
    INSERT @PublicationState SELECT PublicationKey, Title, LastRetrievedAtUtc FROM academiconline.Publication;
    BEGIN TRY
        EXEC academiconline._SubmitAuthorizedDoiFixture @ActorId, @ActorName;
        THROW 52201, 'Expected DOI authorization error 51403.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51403 THROW;
    END CATCH;
    IF @@TRANCOUNT <> 0 OR XACT_STATE() <> 0
        THROW 52201, 'Rejected submission left an open transaction.', 1;
    IF @Publications <> (SELECT COUNT_BIG(*) FROM academiconline.Publication)
       OR @Candidates <> (SELECT COUNT_BIG(*) FROM academiconline.FacultyPublicationReviewCandidate)
       OR @Audits <> (SELECT COUNT_BIG(*) FROM academiconline.FacultyPublicationManualReviewSubmission)
       OR EXISTS
          (SELECT PublicationKey, Title, LastRetrievedAtUtc FROM academiconline.Publication
           EXCEPT SELECT PublicationKey, Title, LastRetrievedAtUtc FROM @PublicationState)
        THROW 52201, 'Rejected submission changed publication, candidate, or submission audit data.', 1;
    PRINT N'PASS: denied without writes: ' + @Case;
END;
GO

EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUser SET IsEnabled = 0 WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'disabled Reviewer with active department access';

EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUser SET IsEnabled = 0 WHERE AppUserId = 13;
EXEC academiconline._ExpectDoiAuthorizationDenial N'disabled Admin', 13, N'fixture.admin';

EXEC academiconline._ResetDoiAuthorizationFixture;
DELETE openalex.AppUserRole WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'Reviewer role removed but access row retained';

EXEC academiconline._ResetDoiAuthorizationFixture;
DELETE openalex.AppUserRole WHERE AppUserId = 13;
EXEC academiconline._ExpectDoiAuthorizationDenial N'Admin role removed', 13, N'fixture.admin';
INSERT openalex.AppUserRole VALUES (13, 2);
EXEC academiconline._ExpectDoiAuthorizationDenial N'Admin demoted to Reviewer without department access', 13, N'fixture.admin';

EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUserDepartmentAccess SET IsActive = 0 WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'department access deactivated';
UPDATE openalex.AppUserDepartmentAccess SET IsActive = 1, CanReview = 0 WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'department review permission removed';
UPDATE openalex.AppUserDepartmentAccess SET CanReview = 1, DepartmentId = 19 WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'access to another department is insufficient';

EXEC academiconline._ResetDoiAuthorizationFixture;
EXEC academiconline._ExpectDoiAuthorizationDenial N'Reader with an otherwise active department row', 14, N'fixture.reader';
EXEC academiconline._ExpectDoiAuthorizationDenial N'missing signed account ID', NULL;
EXEC academiconline._ExpectDoiAuthorizationDenial N'invalid signed account ID', 0;
EXEC academiconline._ExpectDoiAuthorizationDenial N'actor ID does not match username', 12, N'fixture.admin';
DELETE openalex.AppUser WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'removed account with remaining role and access rows';
INSERT openalex.AppUser VALUES (99, N'fixture.reviewer', 1);
EXEC academiconline._ExpectDoiAuthorizationDenial N'username reused for a different account';

-- Legacy callers that omit the new parameter must also fail closed.
EXEC academiconline._ResetDoiAuthorizationFixture;
BEGIN TRY
    EXEC academiconline._SubmitManualDoiFixture;
    THROW 52201, 'Legacy submission unexpectedly bypassed the signed actor ID.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 51403 THROW;
END CATCH;
IF EXISTS (SELECT 1 FROM academiconline.Publication)
   OR EXISTS (SELECT 1 FROM academiconline.FacultyPublicationManualReviewSubmission)
    THROW 52201, 'Legacy caller changed submission data.', 1;
PRINT N'PASS: legacy callers cannot bypass account-ID binding';

DECLARE @Submission TABLE
(
    CandidateId bigint, PublicationKey bigint, WasCreated bit, SubmissionStatus nvarchar(40)
);
INSERT @Submission EXEC academiconline._SubmitAuthorizedDoiFixture;
IF NOT EXISTS (SELECT 1 FROM @Submission WHERE WasCreated = 1 AND SubmissionStatus = N'CreatedPendingCandidate')
    THROW 52201, 'Current Reviewer with department access could not submit.', 1;
DECLARE @CandidateId bigint = (SELECT CandidateId FROM @Submission);
DELETE @Submission;
INSERT @Submission EXEC academiconline._SubmitAuthorizedDoiFixture;
IF NOT EXISTS (SELECT 1 FROM @Submission WHERE CandidateId = @CandidateId AND WasCreated = 0 AND SubmissionStatus = N'AlreadyExists')
    THROW 52201, 'Authorized resubmission changed the idempotent result.', 1;
PRINT N'PASS: current Reviewer and authorized AlreadyExists result';

-- Existing candidate identity must also be in the authorized department.
UPDATE academiconline.FacultyPublicationReviewCandidate SET DepartmentId = 19;
EXEC academiconline._ExpectDoiAuthorizationDenial N'cannot return an existing candidate in another department';
UPDATE academiconline.FacultyPublicationReviewCandidate SET DepartmentId = 18;
UPDATE openalex.AppUser SET IsEnabled = 0 WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'revocation also denies AlreadyExists submissions';

EXEC academiconline._ResetDoiAuthorizationFixture;
DELETE @Submission;
INSERT @Submission EXEC academiconline._SubmitAuthorizedDoiFixture 13, N'fixture.admin';
IF NOT EXISTS (SELECT 1 FROM @Submission WHERE WasCreated = 1)
    THROW 52201, 'Current enabled Admin without department access could not submit.', 1;
PRINT N'PASS: current enabled Admin retains global authority';

-- New auth and the deployed lifecycle fix work together without auto-publication.
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate
               WHERE IsCurrentSource = 1 AND RelationshipSource = N'ManualDoiLookup')
   OR EXISTS (SELECT 1 FROM academiconline.DepartmentRosterPublication)
   OR EXISTS (SELECT 1 FROM academiconline.DepartmentScholarPublication)
    THROW 52201, 'Authorized submission/lifecycle interaction changed source relationships.', 1;
PRINT N'PASS: authorized DOI submission remains current through lifecycle synchronization';
PRINT N'ALL DOI SUBMISSION AUTHORIZATION CHECKS PASSED';
