/*
V3E PATCH021H — Verified Coauthor Identity Recovery
SQL Server 2016 compatible.

Adds explicit resolution audit fields to the official roster mapping.
No identity is inserted by this migration.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.DepartmentRosterMember', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentRosterMember does not exist. Apply PATCH021F first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentRosterMember',
       N'ResolutionMethod') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentRosterMember
        ADD ResolutionMethod NVARCHAR(50) NULL;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentRosterMember',
       N'ResolutionEvidence') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentRosterMember
        ADD ResolutionEvidence NVARCHAR(MAX) NULL;
END;

GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

UPDATE academiconline.DepartmentRosterMember
SET
    ResolutionMethod =
        COALESCE(ResolutionMethod, N'Pre-PATCH021H cached identity'),
    ResolutionEvidence =
        COALESCE(
            ResolutionEvidence,
            N'Academic Online author ID was resolved and cached before PATCH021H.')
WHERE AcademicOnlineAuthorId IS NOT NULL
  AND ResolutionStatus = N'Resolved';

PRINT N'PATCH021H roster resolution audit fields are ready.';
