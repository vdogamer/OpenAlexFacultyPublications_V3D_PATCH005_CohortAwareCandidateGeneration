/* READ ONLY. Run against the selected application or scoped validation database.
   Missing metadata can also mean the caller lacks VIEW DEFINITION permission. */
SET NOCOUNT ON;
SELECT ServerProductVersion = CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')),
       DatabaseName = DB_NAME(), CompatibilityLevel = compatibility_level,
       DatabaseCollation = CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), N'Collation'))
FROM sys.databases WHERE database_id = DB_ID();
SELECT RequiredObject = required.ObjectName,
       Readiness = CASE WHEN OBJECT_ID(required.ObjectName, required.ObjectType) IS NOT NULL
                        THEN N'Present' ELSE N'Missing or not visible' END
FROM (VALUES
    (N'openalex.AppUser', N'U'), (N'openalex.AppRole', N'U'), (N'openalex.AppUserRole', N'U'),
    (N'openalex.AuthenticationEvent', N'U'), (N'openalex.AppUserDepartmentAccess', N'U'),
    (N'openalex.AppUserDepartmentAccessAudit', N'U'),
    (N'academiconline.FacultyPublicationReviewCandidate', N'U'),
    (N'academiconline.FacultyPublicationReviewDecision', N'U'),
    (N'academiconline.FacultyPublicationManualReviewSubmission', N'U'),
    (N'academiconline.FacultyPublicationFreshnessScanAudit', N'U'),
    (N'academiconline.vw_FacultyPublicationSearch', N'V'),
    (N'academiconline.vw_FacultyPublicationReviewUniverse', N'V'),
    (N'academiconline.vw_FacultyPublicationReviewStatus', N'V'),
    (N'academiconline.usp_SetFacultyPublicationReviewDecision', N'P'),
    (N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'P'),
    (N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi', N'P')
) required(ObjectName, ObjectType)
ORDER BY required.ObjectName;

SELECT RequiredColumn = required.ObjectName + N'.' + required.ColumnName,
       Readiness = CASE WHEN COL_LENGTH(required.ObjectName, required.ColumnName) IS NOT NULL
                        THEN N'Present' ELSE N'Missing or not visible' END
FROM (VALUES
    (N'openalex.AppUser', N'IsEnabled'), (N'openalex.AppRole', N'IsEnabled'),
    (N'openalex.AppUserRole', N'IsEnabled'), (N'openalex.AppUserRole', N'RevokedAtUtc')
) required(ObjectName, ColumnName);

SELECT RequiredGuard = required.GuardName,
       Readiness = CASE WHEN CHARINDEX(required.Marker, OBJECT_DEFINITION(OBJECT_ID(required.ObjectName))) > 0
                        THEN N'Present' ELSE N'Missing or not visible' END
FROM (VALUES
    (N'450 NoCandidates', N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'EmptyDepartmentReviewSync_NoCandidates_v1'),
    (N'457 manual lifecycle', N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'ManualDoiReviewLifecycle_v1_BEGIN'),
    (N'458 live submitter', N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi', N'LiveDoiSubmissionAuthorization_v1_BEGIN'),
    (N'459 effective role state', N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi', N'EnabledRoleStateAuthorization_v1')
) required(GuardName, ObjectName, Marker);

IF COL_LENGTH(N'openalex.AppUser', N'IsEnabled') IS NOT NULL
   AND COL_LENGTH(N'openalex.AppRole', N'IsEnabled') IS NOT NULL
   AND COL_LENGTH(N'openalex.AppUserRole', N'IsEnabled') IS NOT NULL
   AND COL_LENGTH(N'openalex.AppUserRole', N'RevokedAtUtc') IS NOT NULL
    EXEC sys.sp_executesql N'
    SELECT EffectiveEnabledAdminCount = COUNT_BIG(*)
    FROM openalex.AppUser u
    JOIN openalex.AppUserRole ur ON ur.AppUserId = u.AppUserId
    JOIN openalex.AppRole r ON r.AppRoleId = ur.AppRoleId
    WHERE u.IsEnabled = 1 AND ur.IsEnabled = 1 AND ur.RevokedAtUtc IS NULL
      AND r.IsEnabled = 1 AND r.RoleName = N''Admin'';';
