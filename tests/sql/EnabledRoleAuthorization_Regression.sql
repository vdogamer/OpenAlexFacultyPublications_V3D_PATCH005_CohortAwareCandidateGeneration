:On Error exit
/* NEW scratch database only; inherited suite checks isolation and earlier fixes. */
:r ./tests/sql/DoiSubmissionAuthorization_Regression.sql
GO
-- Reproduce the state revealed by the actual exported role tables.
EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUserRole SET IsEnabled = 0 WHERE AppUserId = 12;
EXEC academiconline._SubmitAuthorizedDoiFixture;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationManualReviewSubmission)
    THROW 52301, 'Expected SQL 458 to reproduce the disabled-assignment gap.', 1;
PRINT N'REPRODUCED: SQL 458 ignores a disabled role assignment';
GO
:r ./sql/459_PATCH022H_EnabledRoleAuthorization_SQL2016.sql
GO
:r ./sql/459_PATCH022H_EnabledRoleAuthorization_SQL2016.sql
GO
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationManualReviewSubmission)
    THROW 52301, 'Installing the role-state guard changed submission data.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_permissions
    WHERE major_id = OBJECT_ID(N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi')
      AND grantee_principal_id = DATABASE_PRINCIPAL_ID(N'DoiAuthorizationFixtureExecutor')
      AND permission_name = N'EXECUTE' AND state = N'G')
    THROW 52301, 'Enabled-role installer lost an existing procedure grant.', 1;

EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUserRole SET IsEnabled = 0 WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'disabled Reviewer assignment';
EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUserRole SET IsEnabled = 0 WHERE AppUserId = 13;
EXEC academiconline._ExpectDoiAuthorizationDenial N'disabled Admin assignment', 13, N'fixture.admin';
EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppRole SET IsEnabled = 0 WHERE RoleName = N'Reviewer';
EXEC academiconline._ExpectDoiAuthorizationDenial N'disabled Reviewer role definition';
EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppRole SET IsEnabled = 0 WHERE RoleName = N'Admin';
EXEC academiconline._ExpectDoiAuthorizationDenial N'disabled Admin role definition', 13, N'fixture.admin';
EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUserRole SET RevokedAtUtc = SYSUTCDATETIME() WHERE AppUserId = 12;
EXEC academiconline._ExpectDoiAuthorizationDenial N'revoked Reviewer assignment even with IsEnabled=1';
EXEC academiconline._ResetDoiAuthorizationFixture;
UPDATE openalex.AppUserRole SET RevokedAtUtc = SYSUTCDATETIME() WHERE AppUserId = 13;
EXEC academiconline._ExpectDoiAuthorizationDenial N'revoked Admin assignment even with IsEnabled=1', 13, N'fixture.admin';
EXEC academiconline._ResetDoiAuthorizationFixture;
EXEC academiconline._SubmitAuthorizedDoiFixture;
EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment 18;
IF NOT EXISTS (SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate WHERE IsCurrentSource = 1)
    THROW 52301, 'Enabled role state broke authorized submission/lifecycle reconciliation.', 1;
PRINT N'ALL ENABLED ROLE AUTHORIZATION CHECKS PASSED';
