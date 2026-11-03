/*
Targets the deployed PATCH021U review-sync procedure, not a fresh PATCH021N DB.
Preserves the existing procedure body, permissions, and six-column result contract.
Only the unique, recognized empty-source guard is extended. No data is modified
by applying this migration. Save the existing procedure definition before use.
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @ProcedureId int = OBJECT_ID(
    N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'P');
DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(@ProcedureId);
DECLARE @Marker nvarchar(100) = N'/* EmptyDepartmentReviewSync_NoCandidates_v1 */';
DECLARE @Anchor nvarchar(100) = N'THROW 51101,';

IF @Definition IS NULL
    THROW 51210, 'Review-sync procedure missing or definition not visible. This patch requires the deployed PATCH021U routine.', 1;

IF CHARINDEX(@Marker, @Definition) > 0
BEGIN
    PRINT N'NoCandidates patch already installed; no change made.';
    RETURN;
END;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.SyncRun', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.DepartmentScholarPublication', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.DepartmentRosterPublication', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.FacultyPublicationReviewCandidate', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.vw_FacultyPublicationSearch', N'V') IS NULL
   OR OBJECT_ID(N'academiconline.vw_FacultyPublicationReviewUniverse', N'V') IS NULL
    THROW 51211, 'Required deployed review-sync objects are missing.', 1;

DECLARE @AnchorPosition int = CHARINDEX(@Anchor, @Definition);
IF @AnchorPosition = 0
   OR CHARINDEX(@Anchor, @Definition, @AnchorPosition + LEN(@Anchor)) > 0
   OR CHARINDEX(N'Review synchronization aborted: no review-universe rows exist for this department.', @Definition) = 0
    THROW 51212, 'Unrecognized review-sync guard. Inspect the current procedure; no change made.', 1;

-- Compare only the guard context. Do not normalize or rewrite the procedure.
DECLARE @Context nvarchar(max) = LEFT(@Definition, @AnchorPosition - 1);
SET @Context = REPLACE(REPLACE(REPLACE(REPLACE(
    @Context, N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'');
IF RIGHT(@Context, LEN(N'IF@SourceRows=0BEGIN')) COLLATE Latin1_General_100_CI_AS
       <> N'IF@SourceRows=0BEGIN'
    THROW 51212, 'Unrecognized empty-source guard context; no change made.', 1;

DECLARE @NoCandidatesGuard nvarchar(max);
SET @NoCandidatesGuard = N'
        /* EmptyDepartmentReviewSync_NoCandidates_v1 */
        -- Empty is expected only for a complete, promoted, zero-publication
        -- snapshot whose faculty source is present and agrees with that run.
        -- Existing candidates and historical relationships remain protected.
        IF EXISTS
        (
            SELECT 1
            FROM academiconline.Department d
            JOIN academiconline.SyncRun sr
              ON sr.AcademicOnlineSyncRunId = d.CurrentSnapshotRunId
            WHERE d.DepartmentId = @DepartmentId
              AND d.IsActive = 1
              AND sr.RunStatus = N''Completed''
              AND sr.IsCacheComplete = 1
              AND sr.SnapshotPromoted = 1
              AND sr.PublicationsProcessed = 0
              AND sr.ScholarsDiscovered > 0
              AND sr.ScholarsProcessed = sr.ScholarsDiscovered
              AND sr.ScholarPaginationSafeguardHit = 0
              AND sr.PublicationPaginationSafeguardHitCount = 0
              AND
              (
                  SELECT COUNT_BIG(*)
                  FROM academiconline.vw_FacultyPublicationSearch f
                  WHERE f.DepartmentId = d.DepartmentId
                    AND f.DepartmentDataComplete = 1
                    AND f.DataRunId = sr.AcademicOnlineSyncRunId
                    AND (f.FacultyKey LIKE N''AO:_%''
                         OR (f.FacultyKey LIKE N''ROSTER:%''
                             AND TRY_CONVERT(bigint, SUBSTRING(
                                 f.FacultyKey, LEN(N''ROSTER:'') + 1, 50)) IS NOT NULL))
              ) = sr.ScholarsDiscovered
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM academiconline.FacultyPublicationReviewCandidate c
                  WHERE c.DepartmentId = d.DepartmentId
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM academiconline.DepartmentScholarPublication dsp
                  WHERE dsp.DepartmentId = d.DepartmentId
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM academiconline.DepartmentRosterPublication drp
                  WHERE drp.DepartmentId = d.DepartmentId
              )
        )
        BEGIN
            PRINT N''NoCandidates: complete promoted snapshot has zero qualifying publications and no prior review associations.'';
            SELECT
                DepartmentId = @DepartmentId,
                SourceRows = @SourceRows,
                UpdatedRows = @UpdatedRows,
                InsertedRows = @InsertedRows,
                RetiredRows = @RetiredRows,
                CurrentCandidateRows = CONVERT(bigint, 0);
            RETURN;
        END;

        ';

SET @Definition = STUFF(@Definition, @AnchorPosition, 0, @NoCandidatesGuard);

-- ALTER preserves the object and grants. Fail closed on unfamiliar headers.
DECLARE @HeaderPosition int = PATINDEX(
    N'%[^ ' + NCHAR(9) + NCHAR(10) + NCHAR(13) + N']%', @Definition);
IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 6)) = N'CREATE'
BEGIN
    DECLARE @HeaderTail nvarchar(max) = LTRIM(SUBSTRING(
        @Definition, @HeaderPosition + 6, LEN(@Definition)));
    SET @HeaderTail = LTRIM(REPLACE(REPLACE(REPLACE(
        @HeaderTail, NCHAR(9), N' '), NCHAR(10), N' '), NCHAR(13), N' '));
    IF LEFT(UPPER(@HeaderTail), 9) <> N'PROCEDURE'
       AND LEFT(UPPER(@HeaderTail), 5) <> N'PROC '
        THROW 51213, 'Unrecognized CREATE header; no change made.', 1;
    SET @Definition = STUFF(@Definition, @HeaderPosition, 6, N'ALTER');
END
ELSE IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 5)) <> N'ALTER'
    THROW 51213, 'Unrecognized procedure header; no change made.', 1;

EXEC sys.sp_executesql @Definition;
PRINT N'Installed NoCandidates guard. Existing nonempty synchronization is unchanged.';
