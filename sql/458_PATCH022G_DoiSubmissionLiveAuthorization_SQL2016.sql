/*
PATCH022G: fail closed on stale-cookie DOI submissions.
Requires PATCH022C and the current openalex application-user/role/access objects.
DDL only; deploy with the matching web application while submission writers are stopped.
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi', N'P'));
DECLARE @Marker nvarchar(100) = N'/* LiveDoiSubmissionAuthorization_v1_BEGIN */';
DECLARE @EndMarker nvarchar(100) = N'/* LiveDoiSubmissionAuthorization_v1_END */';
DECLARE @ParameterAnchor nvarchar(100) = N'@SubmittedBy NVARCHAR(256)';
DECLARE @TransactionAnchor nvarchar(100) = N'BEGIN TRANSACTION;';
DECLARE @CandidateAnchor nvarchar(100) = N'IF @CandidateId IS NULL';

IF @Definition IS NULL
    THROW 51410, 'The PATCH022C DOI submission procedure is missing or not visible.', 1;
IF CHARINDEX(@Marker, @Definition) > 0
BEGIN
    IF CHARINDEX(@EndMarker, @Definition) < CHARINDEX(@Marker, @Definition)
       OR CHARINDEX(N'@SubmittedByAppUserId', @Definition) = 0
       OR CHARINDEX(N'/* LiveDoiSubmissionCandidateScope_v1 */', @Definition) = 0
        THROW 51411, 'Incomplete DOI authorization patch; inspect the procedure.', 1;
    PRINT N'Live DOI submission authorization already installed; no change made.';
    RETURN;
END;
IF OBJECT_ID(N'openalex.AppUser', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AppUserRole', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AppRole', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AppUserDepartmentAccess', N'U') IS NULL
    THROW 51410, 'Current application user, role, and department-access tables are required.', 1;
IF CHARINDEX(@EndMarker, @Definition) > 0
   OR CHARINDEX(N'@SubmittedByAppUserId', @Definition) > 0
   OR CHARINDEX(N'FacultyPublicationManualReviewSubmission', @Definition) = 0
   OR CHARINDEX(N'SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;', @Definition) = 0
    THROW 51411, 'Unrecognized DOI submission procedure; no change made.', 1;

DECLARE @ParameterPosition int = CHARINDEX(@ParameterAnchor, @Definition COLLATE Latin1_General_100_CI_AS);
DECLARE @TransactionPosition int = CHARINDEX(@TransactionAnchor, @Definition COLLATE Latin1_General_100_CI_AS);
DECLARE @CandidatePosition int = CHARINDEX(@CandidateAnchor, @Definition COLLATE Latin1_General_100_CI_AS);
IF @ParameterPosition = 0 OR @TransactionPosition = 0 OR @CandidatePosition = 0
   OR @CandidatePosition < @TransactionPosition
   OR CHARINDEX(@ParameterAnchor, @Definition COLLATE Latin1_General_100_CI_AS, @ParameterPosition + LEN(@ParameterAnchor)) > 0
   OR CHARINDEX(@TransactionAnchor, @Definition COLLATE Latin1_General_100_CI_AS, @TransactionPosition + LEN(@TransactionAnchor)) > 0
   OR CHARINDEX(@CandidateAnchor, @Definition COLLATE Latin1_General_100_CI_AS, @CandidatePosition + LEN(@CandidateAnchor)) > 0
    THROW 51411, 'Unrecognized submission parameter/transaction/candidate layout; no change made.', 1;

DECLARE @AuthorizationBlock nvarchar(max) = N'
        /* LiveDoiSubmissionAuthorization_v1_BEGIN */
        -- The server supplies both signed actor fields; never authorize a
        -- username newly assigned to a different account. HOLDLOCK keeps live
        -- account, role, and scope reads valid until this transaction commits,
        -- including under READ_COMMITTED_SNAPSHOT.
        DECLARE @LiveSubmitterId bigint, @LiveIsAdmin bit = 0, @LiveIsReviewer bit = 0;
        IF @SubmittedByAppUserId IS NULL OR @SubmittedByAppUserId <= 0
            THROW 51403, ''A current authenticated application account is required for DOI submission.'', 1;

        SELECT @LiveSubmitterId = u.AppUserId
        FROM openalex.AppUser u WITH (HOLDLOCK)
        WHERE u.AppUserId = @SubmittedByAppUserId
          AND u.UserName = @SubmittedBy
          AND u.IsEnabled = 1;

        IF @LiveSubmitterId IS NULL
            THROW 51403, ''The submission account is missing, disabled, or no longer matches the authenticated identity.'', 1;

        SELECT
            @LiveIsAdmin = COALESCE(MAX(CASE WHEN r.RoleName = N''Admin'' THEN 1 ELSE 0 END), 0),
            @LiveIsReviewer = COALESCE(MAX(CASE WHEN r.RoleName = N''Reviewer'' THEN 1 ELSE 0 END), 0)
        FROM openalex.AppUserRole ur WITH (HOLDLOCK)
        JOIN openalex.AppRole r WITH (HOLDLOCK) ON r.AppRoleId = ur.AppRoleId
        WHERE ur.AppUserId = @LiveSubmitterId;

        IF @LiveIsAdmin = 0 AND
           (@LiveIsReviewer = 0 OR NOT EXISTS
            (
                SELECT 1 FROM openalex.AppUserDepartmentAccess a WITH (HOLDLOCK)
                WHERE a.AppUserId = @LiveSubmitterId
                  AND a.DepartmentId = @DepartmentId
                  AND a.IsActive = 1 AND a.CanReview = 1
            ))
            THROW 51403, ''The current account is not authorized to submit publications for this department.'', 1;
        /* LiveDoiSubmissionAuthorization_v1_END */
';
DECLARE @CandidateScopeBlock nvarchar(max) = N'/* LiveDoiSubmissionCandidateScope_v1 */
        IF @CandidateId IS NOT NULL AND NOT EXISTS
        (
            SELECT 1 FROM academiconline.FacultyPublicationReviewCandidate c WITH (HOLDLOCK)
            WHERE c.FacultyPublicationReviewCandidateId = @CandidateId
              AND c.DepartmentId = @DepartmentId
        )
            THROW 51403, ''The existing candidate does not belong to the authorized department.'', 1;

        ';
-- Work from the last anchor backwards, preserving the complete original body.
SET @Definition = STUFF(@Definition, @CandidatePosition, 0, @CandidateScopeBlock);
SET @Definition = STUFF(@Definition, @TransactionPosition + LEN(@TransactionAnchor), 0, @AuthorizationBlock);
SET @Definition = STUFF(@Definition, @ParameterPosition + LEN(@ParameterAnchor), 0,
    N',' + NCHAR(10) + N'    @SubmittedByAppUserId bigint = NULL');

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
            THROW 51411, 'Unterminated procedure header comment; no change made.', 1;
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
        THROW 51411, 'Unrecognized CREATE header; no change made.', 1;
    SET @Definition = STUFF(@Definition, @HeaderPosition, 6, N'ALTER');
END
ELSE IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 5)) <> N'ALTER'
    THROW 51411, 'Unrecognized procedure header; no change made.', 1;



EXEC sys.sp_executesql @Definition;
PRINT N'Installed live DOI submission authorization. No publication or candidate data was changed by installation.';
