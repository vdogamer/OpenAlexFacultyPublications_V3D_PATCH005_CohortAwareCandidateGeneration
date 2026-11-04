/* PATCH022F rollback: remove only the manual-source block; retain the 450 guard.
   DDL only. Does not undo candidate changes made by intervening synchronizations. */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'P'));
DECLARE @BeginMarker nvarchar(100) = N'/* ManualDoiReviewLifecycle_v1_BEGIN */';
DECLARE @EndMarker nvarchar(100) = N'/* ManualDoiReviewLifecycle_v1_END */';
DECLARE @Anchor nvarchar(100) = N'IF @SourceRows = 0';

IF @Definition IS NULL
    THROW 51310, 'Review-sync definition is unavailable; no change made.', 1;
DECLARE @StartPosition int = CHARINDEX(@BeginMarker, @Definition);
IF @StartPosition = 0
BEGIN
    IF CHARINDEX(@EndMarker, @Definition) > 0
        THROW 51314, 'Incomplete manual-lifecycle patch marker; inspect the procedure.', 1;
    PRINT N'Manual DOI lifecycle patch is not installed; no change made.';
    RETURN;
END;
DECLARE @EndPosition int = CHARINDEX(@EndMarker, @Definition, @StartPosition);
IF @EndPosition = 0
   OR CHARINDEX(@BeginMarker, @Definition, @StartPosition + LEN(@BeginMarker)) > 0
   OR CHARINDEX(@EndMarker, @Definition, @EndPosition + LEN(@EndMarker)) > 0
   OR CHARINDEX(N'/* EmptyDepartmentReviewSync_NoCandidates_v1 */', @Definition) = 0
    THROW 51314, 'Unrecognized installed lifecycle policy; no change made.', 1;
SET @EndPosition += LEN(@EndMarker);
DECLARE @AnchorPosition int = CHARINDEX(@Anchor, @Definition, @EndPosition);
IF @AnchorPosition = 0
    THROW 51314, 'Original empty-source guard was not found; no change made.', 1;
DECLARE @Padding nvarchar(max) = SUBSTRING(
    @Definition, @EndPosition, @AnchorPosition - @EndPosition);
SET @Padding = REPLACE(REPLACE(REPLACE(REPLACE(
    @Padding, N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'');
IF LEN(@Padding) <> 0
    THROW 51314, 'Unexpected edits after the lifecycle block; inspect before rollback.', 1;

SET @Definition = STUFF(@Definition, @StartPosition, @AnchorPosition - @StartPosition, N'');
-- OBJECT_DEFINITION can retain comments preceding CREATE/ALTER. Skip only
-- header trivia; never search for keywords that could occur inside comments.
DECLARE @HeaderPosition int = 1;
DECLARE @DefinitionLength int = LEN(@Definition);
DECLARE @CommentDepth int;
WHILE @HeaderPosition <= @DefinitionLength
BEGIN
    IF UNICODE(SUBSTRING(@Definition, @HeaderPosition, 1)) IN (9, 10, 11, 12, 13, 32, 65279)
    BEGIN
        SET @HeaderPosition += 1;
        CONTINUE;
    END;
    IF SUBSTRING(@Definition, @HeaderPosition, 2) = N'--'
    BEGIN
        SET @HeaderPosition += 2;
        WHILE @HeaderPosition <= @DefinitionLength
          AND UNICODE(SUBSTRING(@Definition, @HeaderPosition, 1)) NOT IN (10, 13)
            SET @HeaderPosition += 1;
        CONTINUE;
    END;
    IF SUBSTRING(@Definition, @HeaderPosition, 2) = N'/*'
    BEGIN
        SET @CommentDepth = 1;
        SET @HeaderPosition += 2;
        WHILE @CommentDepth > 0 AND @HeaderPosition <= @DefinitionLength
        BEGIN
            IF SUBSTRING(@Definition, @HeaderPosition, 2) = N'/*'
            BEGIN
                SET @CommentDepth += 1;
                SET @HeaderPosition += 2;
            END
            ELSE IF SUBSTRING(@Definition, @HeaderPosition, 2) = N'*/'
            BEGIN
                SET @CommentDepth -= 1;
                SET @HeaderPosition += 2;
            END
            ELSE SET @HeaderPosition += 1;
        END;
        IF @CommentDepth <> 0
            THROW 51314, 'Unterminated procedure header comment; no change made.', 1;
        CONTINUE;
    END;
    BREAK;
END;

-- ALTER preserves the object and grants. Fail closed on unfamiliar headers.
IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 6)) = N'CREATE'
BEGIN
    DECLARE @HeaderTail nvarchar(max) = LTRIM(SUBSTRING(
        @Definition, @HeaderPosition + 6, LEN(@Definition)));
    SET @HeaderTail = LTRIM(REPLACE(REPLACE(REPLACE(
        @HeaderTail, NCHAR(9), N' '), NCHAR(10), N' '), NCHAR(13), N' '));
    IF LEFT(UPPER(@HeaderTail), 9) <> N'PROCEDURE'
       AND LEFT(UPPER(@HeaderTail), 5) <> N'PROC '
        THROW 51314, 'Unrecognized CREATE header; no change made.', 1;
    SET @Definition = STUFF(@Definition, @HeaderPosition, 6, N'ALTER');
END
ELSE IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 5)) <> N'ALTER'
    THROW 51314, 'Unrecognized procedure header; no change made.', 1;


EXEC sys.sp_executesql @Definition;
PRINT N'Removed manual DOI lifecycle policy; the 450 NoCandidates guard remains.';
