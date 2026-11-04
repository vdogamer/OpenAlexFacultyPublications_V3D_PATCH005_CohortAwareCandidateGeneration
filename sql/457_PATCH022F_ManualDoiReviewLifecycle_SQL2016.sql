/*
PATCH022F: preserve auditable manual DOI candidates during review synchronization.
Requires deployed PATCH022C objects and the 450 NoCandidates guard.
DDL only: applying this script does not execute review sync or mutate candidates.
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @ProcedureId int = OBJECT_ID(
    N'academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment', N'P');
DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(@ProcedureId);
DECLARE @BeginMarker nvarchar(100) = N'/* ManualDoiReviewLifecycle_v1_BEGIN */';
DECLARE @EndMarker nvarchar(100) = N'/* ManualDoiReviewLifecycle_v1_END */';
DECLARE @Anchor nvarchar(100) = N'IF @SourceRows = 0';

IF @Definition IS NULL
    THROW 51310, 'The deployed review-sync procedure is missing or not visible.', 1;
IF CHARINDEX(@BeginMarker, @Definition) > 0
BEGIN
    DECLARE @InstalledStart int = CHARINDEX(@BeginMarker, @Definition);
    DECLARE @InstalledEnd int = CHARINDEX(@EndMarker, @Definition);
    IF @InstalledEnd < @InstalledStart
       OR CHARINDEX(@BeginMarker, @Definition, @InstalledStart + LEN(@BeginMarker)) > 0
       OR CHARINDEX(@EndMarker, @Definition, @InstalledEnd + LEN(@EndMarker)) > 0
        THROW 51311, 'Incomplete manual-lifecycle patch marker; inspect the procedure.', 1;
    PRINT N'Manual DOI lifecycle patch already installed; no change made.';
    RETURN;
END;
IF CHARINDEX(@EndMarker, @Definition) > 0
    THROW 51311, 'Orphaned manual-lifecycle end marker; inspect the procedure.', 1;
IF CHARINDEX(N'/* EmptyDepartmentReviewSync_NoCandidates_v1 */', @Definition) = 0
    THROW 51311, 'Apply the 450 NoCandidates fix before this patch.', 1;
IF OBJECT_ID(N'academiconline.FacultyPublicationManualReviewSubmission', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.Publication', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.vw_FacultyPublicationSearch', N'V') IS NULL
    THROW 51311, 'PATCH022C manual submission and publication/faculty objects are required.', 1;

-- This patch targets the supplied automatic AO/roster review universe.
DECLARE @UniverseDefinition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(
    N'academiconline.vw_FacultyPublicationReviewUniverse', N'V'));
IF @UniverseDefinition IS NULL
   OR CHARINDEX(N'FacultyPublicationManualReviewSubmission', @UniverseDefinition) > 0
   OR CHARINDEX(N'ManualDoiLookup', @UniverseDefinition) > 0
    THROW 51311, 'Unrecognized review universe; review manual-source integration before applying.', 1;

DECLARE @AnchorPosition int = CHARINDEX(@Anchor, @Definition);
IF @AnchorPosition = 0
   OR CHARINDEX(@Anchor, @Definition, @AnchorPosition + LEN(@Anchor)) > 0
   OR CHARINDEX(N'FROM #CurrentReviewSource', LEFT(@Definition, @AnchorPosition)) = 0
   OR CHARINDEX(N'#ManualDoiReviewSource', @Definition) > 0
    THROW 51312, 'Unrecognized source/empty-guard layout; no change made.', 1;

DECLARE @ManualSourceBlock nvarchar(max);
SET @ManualSourceBlock = N'/* ManualDoiReviewLifecycle_v1_BEGIN */
    -- Only an audit of the original creation establishes manual provenance.
    -- An AlreadyExists audit must not turn an automatic candidate into a manual one.
    SELECT
        c.FacultyPublicationReviewCandidateId,
        c.ReviewSourceKey,
        c.FacultyKey,
        c.PublicationKey,
        c.DepartmentId,
        f.DepartmentName,
        f.DivisionName,
        f.UvaUserId,
        f.DisplayName AS FacultyDisplayName,
        f.Email,
        f.Position,
        f.Orcid,
        CONVERT(nvarchar(30), N''OpenAlex'') AS PublicationIdentitySource,
        a.OpenAlexAuthorId AS SourceAuthorId,
        CONVERT(nvarchar(60), N''ManualDoiLookup'') AS RelationshipSource,
        CONVERT(nvarchar(60), N''OpenAlex'') AS RelationshipSourceProvider,
        a.OpenAlexAuthorId AS RelationshipSourceAuthorId,
        p.AcademicOnlinePublicationId,
        COALESCE(p.OpenAlexId, a.OpenAlexId) AS OpenAlexId,
        p.Doi,
        p.DoiNormalized,
        p.Title,
        p.Venue,
        p.YearText,
        p.PublicationYear,
        p.CitationCount,
        p.Verified,
        p.Link,
        f.DataRunId,
        CONVERT(bigint, NULL) AS RelationshipSnapshotRunId,
        CONVERT(bit, 0) AS IsInCurrentReportingWindow,
        f.DepartmentDataComplete
    INTO #ManualDoiReviewSource
    FROM academiconline.FacultyPublicationReviewCandidate c
    JOIN academiconline.Publication p
      ON p.PublicationKey = c.PublicationKey
    JOIN academiconline.Department d
      ON d.DepartmentId = c.DepartmentId
     AND d.IsActive = 1
    CROSS APPLY
    (
        SELECT TOP (1) s.OpenAlexAuthorId, s.OpenAlexId
        FROM academiconline.FacultyPublicationManualReviewSubmission s
        WHERE s.FacultyPublicationReviewCandidateId = c.FacultyPublicationReviewCandidateId
          AND s.PublicationKey = c.PublicationKey
          AND s.FacultyKey = c.FacultyKey
          AND s.DepartmentId = c.DepartmentId
          AND s.SubmissionStatus = N''CreatedPendingCandidate''
          AND NULLIF(LTRIM(RTRIM(s.OpenAlexAuthorId)), N'''') IS NOT NULL
        ORDER BY s.FacultyPublicationManualReviewSubmissionId
    ) a
    CROSS APPLY
    (
        SELECT TOP (1) f.*
        FROM academiconline.vw_FacultyPublicationSearch f
        WHERE f.FacultyKey = c.FacultyKey
          AND f.DepartmentId = c.DepartmentId
          AND f.DepartmentDataComplete = 1
          AND f.DataRunId = d.CurrentSnapshotRunId
        ORDER BY f.DataRunId DESC, f.DisplayName, f.Email
    ) f
    WHERE c.DepartmentId = @DepartmentId
      AND NOT EXISTS
      (
          SELECT 1 FROM #CurrentReviewSource s
          WHERE s.FacultyKey = c.FacultyKey
            AND s.PublicationKey = c.PublicationKey
      );

    -- A manual row must not hide an unexpectedly empty automatic source.
    -- Permit a manual-only synchronization only for the same expected-empty
    -- snapshot proven by the NoCandidates fix, with every existing candidate
    -- accounted for by an eligible manual creation audit.
    IF @SourceRows = 0 AND EXISTS (SELECT 1 FROM #ManualDoiReviewSource)
       AND NOT EXISTS
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
                   AND NOT EXISTS
                   (
                       SELECT 1 FROM #ManualDoiReviewSource m
                       WHERE m.FacultyPublicationReviewCandidateId =
                             c.FacultyPublicationReviewCandidateId
                   )
             )
             AND NOT EXISTS
             (
                 SELECT 1 FROM academiconline.DepartmentScholarPublication s
                 WHERE s.DepartmentId = d.DepartmentId
             )
             AND NOT EXISTS
             (
                 SELECT 1 FROM academiconline.DepartmentRosterPublication s
                 WHERE s.DepartmentId = d.DepartmentId
             )
       )
    BEGIN
        THROW 51101, ''Review synchronization aborted: automatic source is unexpectedly empty; manual candidates do not bypass source safety checks.'', 1;
    END;

    INSERT INTO #CurrentReviewSource
    (
        ReviewSourceKey,
        FacultyKey,
        PublicationKey,
        DepartmentId,
        DepartmentName,
        DivisionName,
        UvaUserId,
        FacultyDisplayName,
        Email,
        Position,
        Orcid,
        PublicationIdentitySource,
        SourceAuthorId,
        RelationshipSource,
        RelationshipSourceProvider,
        RelationshipSourceAuthorId,
        AcademicOnlinePublicationId,
        OpenAlexId,
        Doi,
        DoiNormalized,
        Title,
        Venue,
        YearText,
        PublicationYear,
        CitationCount,
        Verified,
        Link,
        DataRunId,
        RelationshipSnapshotRunId,
        IsInCurrentReportingWindow,
        DepartmentDataComplete
    )
    SELECT
        m.ReviewSourceKey,
        m.FacultyKey,
        m.PublicationKey,
        m.DepartmentId,
        m.DepartmentName,
        m.DivisionName,
        m.UvaUserId,
        m.FacultyDisplayName,
        m.Email,
        m.Position,
        m.Orcid,
        m.PublicationIdentitySource,
        m.SourceAuthorId,
        m.RelationshipSource,
        m.RelationshipSourceProvider,
        m.RelationshipSourceAuthorId,
        m.AcademicOnlinePublicationId,
        m.OpenAlexId,
        m.Doi,
        m.DoiNormalized,
        m.Title,
        m.Venue,
        m.YearText,
        m.PublicationYear,
        m.CitationCount,
        m.Verified,
        m.Link,
        m.DataRunId,
        m.RelationshipSnapshotRunId,
        m.IsInCurrentReportingWindow,
        m.DepartmentDataComplete
    FROM #ManualDoiReviewSource m;

    SELECT @SourceRows = COUNT_BIG(*) FROM #CurrentReviewSource;
    /* ManualDoiReviewLifecycle_v1_END */
    ';

SET @Definition = STUFF(@Definition, @AnchorPosition, 0, @ManualSourceBlock);
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
            THROW 51313, 'Unterminated procedure header comment; no change made.', 1;
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
        THROW 51313, 'Unrecognized CREATE header; no change made.', 1;
    SET @Definition = STUFF(@Definition, @HeaderPosition, 6, N'ALTER');
END
ELSE IF UPPER(SUBSTRING(@Definition, @HeaderPosition, 5)) <> N'ALTER'
    THROW 51313, 'Unrecognized procedure header; no change made.', 1;


EXEC sys.sp_executesql @Definition;
PRINT N'Installed manual DOI review lifecycle policy. No candidates were changed by installation.';
