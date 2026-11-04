:On Error exit
/* NEW EMPTY DATABASE ONLY; sqlcmd -b, repository root working directory.
   No application data, users, role memberships or service configuration seeded.
   Use the dedicated regression entry point for scratch database validation. */
:r ./sql/baselines/PATCH022_ReviewAuthPrerequisites_SQL2016.sql
GO
:r ./sql/150_V3C_Create_Role_Management_Extensions.sql
GO
:r ./sql/452_PATCH022C_SendToReview_SQL2016.sql
GO
:r ./sql/455_PATCH022E_PublicationFreshnessScanAudit_SQL2016.sql
GO
-- The snapshot already has 450/457; these must return explicit no-change outcomes.
:r ./sql/450_EmptyDepartmentReviewSync_NoCandidates_SQL2016.sql
GO
:r ./sql/457_PATCH022F_ManualDoiReviewLifecycle_SQL2016.sql
GO
:r ./sql/458_PATCH022G_DoiSubmissionLiveAuthorization_SQL2016.sql
GO
:r ./sql/459_PATCH022H_EnabledRoleAuthorization_SQL2016.sql
GO
EXEC sys.sp_refreshview N'academiconline.vw_FacultyPublicationSearch';
EXEC sys.sp_refreshview N'academiconline.vw_FacultyPublicationReviewUniverse';
EXEC sys.sp_refreshview N'academiconline.vw_FacultyPublicationReviewStatus';
EXEC sys.sp_refreshsqlmodule N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment';
EXEC sys.sp_refreshsqlmodule N'academiconline.usp_SetFacultyPublicationReviewDecision';
EXEC sys.sp_refreshsqlmodule N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi';
PRINT N'INSTALLED SCOPED PATCH022 REVIEW/AUTH BASELINE; NO APPLICATION DATA OR PRIVILEGES SEEDED';
