/*
V3E PATCH021I — OpenAlex Fallback Identity Preview
SQL Server 2016 compatible.

Adds separate, preview-only OpenAlex identity fields to FacultyRoster members.
These fields do NOT make a roster member cache-complete and are not used by
the Academic Online publication sync in PATCH021I.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(
       N'academiconline.DepartmentRosterMember',
       N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentRosterMember does not exist. Apply PATCH021F first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentRosterMember',
       N'OpenAlexAuthorId') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentRosterMember
        ADD OpenAlexAuthorId NVARCHAR(128) NULL;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentRosterMember',
       N'OpenAlexResolutionStatus') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentRosterMember
        ADD OpenAlexResolutionStatus NVARCHAR(50) NULL;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentRosterMember',
       N'OpenAlexResolutionEvidence') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentRosterMember
        ADD OpenAlexResolutionEvidence NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentRosterMember',
       N'OpenAlexLastResolvedAtUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentRosterMember
        ADD OpenAlexLastResolvedAtUtc DATETIME2(3) NULL;
END;

GO

SET NOCOUNT ON;

PRINT N'PATCH021I OpenAlex fallback preview fields are ready.';
