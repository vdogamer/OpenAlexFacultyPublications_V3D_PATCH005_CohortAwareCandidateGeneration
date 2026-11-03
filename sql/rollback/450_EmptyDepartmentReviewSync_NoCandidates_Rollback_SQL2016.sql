/* Remove only the guard installed by 450. Does not modify application data. */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'P'));
DECLARE @Marker nvarchar(100) = N'/* EmptyDepartmentReviewSync_NoCandidates_v1 */';
IF @Definition IS NULL
    THROW 51210, 'Review-sync definition is unavailable; no change made.', 1;
DECLARE @MarkerPosition int = CHARINDEX(@Marker, @Definition);
IF @MarkerPosition = 0
BEGIN
    PRINT N'NoCandidates patch is not installed; no change made.';
    RETURN;
END;

DECLARE @ThrowPosition int = CHARINDEX(N'THROW 51101,', @Definition, @MarkerPosition);
DECLARE @Prefix nvarchar(20) = NCHAR(10) + REPLICATE(N' ', 8);
DECLARE @StartPosition int = @MarkerPosition - LEN(@Prefix + N'x') + 1;
IF @ThrowPosition = 0 OR @StartPosition < 1
   OR SUBSTRING(@Definition, @StartPosition, LEN(@Prefix + N'x') - 1) <> @Prefix
   OR CHARINDEX(@Marker, @Definition, @MarkerPosition + LEN(@Marker)) > 0
    THROW 51214, 'Unrecognized installed guard; restore the saved definition after review.', 1;

SET @Definition = STUFF(@Definition, @StartPosition, @ThrowPosition - @StartPosition, N'');
-- Preserve leading line and nested block comments in the module definition.
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
            THROW 51214, 'Unterminated procedure header comment; no change made.', 1;
        CONTINUE;
    END;
    BREAK;
END;
IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 5)) <> N'ALTER'
    THROW 51214, 'Unrecognized patched procedure header; no change made.', 1;

EXEC sys.sp_executesql @Definition;
PRINT N'Restored the original empty-source guard. No application data was changed.';
