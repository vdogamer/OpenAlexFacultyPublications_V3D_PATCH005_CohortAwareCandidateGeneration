/*
PATCH022H: honor enabled/revoked state on the actual deployed auth schema.
Requires installed SQL 458 and the exported IsEnabled/RevokedAtUtc columns.
DDL only; deploy with the matching Web build while submission writers are stopped.
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi', N'P'));
DECLARE @Marker nvarchar(100) = N'/* EnabledRoleStateAuthorization_v1 */';
DECLARE @Anchor nvarchar(200) = N'WHERE ur.AppUserId = @LiveSubmitterId;';
IF @Definition IS NULL
   OR CHARINDEX(N'/* LiveDoiSubmissionAuthorization_v1_BEGIN */', @Definition) = 0
    THROW 51510, 'Apply SQL 458 before the enabled-role-state guard.', 1;
IF COL_LENGTH(N'openalex.AppUserRole', N'IsEnabled') IS NULL
   OR COL_LENGTH(N'openalex.AppUserRole', N'RevokedAtUtc') IS NULL
   OR COL_LENGTH(N'openalex.AppRole', N'IsEnabled') IS NULL
    THROW 51510, 'The deployed enabled/revoked role-state columns are required.', 1;
IF CHARINDEX(@Marker, @Definition) > 0
BEGIN
    PRINT N'Enabled-role-state authorization already installed; no change made.';
    RETURN;
END;
DECLARE @Position int = CHARINDEX(@Anchor, @Definition);
IF @Position = 0 OR CHARINDEX(@Anchor, @Definition, @Position + LEN(@Anchor)) > 0
    THROW 51511, 'Unrecognized live-role lookup; no change made.', 1;
DECLARE @Replacement nvarchar(max) = N'WHERE ur.AppUserId = @LiveSubmitterId
          AND ur.IsEnabled = 1
          AND ur.RevokedAtUtc IS NULL
          AND r.IsEnabled = 1;
        /* EnabledRoleStateAuthorization_v1 */';
SET @Definition = STUFF(@Definition, @Position, LEN(@Anchor), @Replacement);

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
            THROW 51511, 'Unterminated procedure header comment; no change made.', 1;
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
        THROW 51511, 'Unrecognized CREATE header; no change made.', 1;
    SET @Definition = STUFF(@Definition, @HeaderPosition, 6, N'ALTER');
END
ELSE IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 5)) <> N'ALTER'
    THROW 51511, 'Unrecognized procedure header; no change made.', 1;




EXEC sys.sp_executesql @Definition;
PRINT N'Installed enabled-role-state authorization. No account, role, publication, candidate, or audit data was changed.';
