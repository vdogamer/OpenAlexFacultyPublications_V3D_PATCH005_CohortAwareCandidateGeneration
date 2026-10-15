/*
PATCH022C — Controlled DOI "Send to Review"
SQL Server 2016 compatible.

What this migration adds
------------------------
1) academiconline.FacultyPublicationManualReviewSubmission
   - audit trail for manual DOI submissions from Publication Freshness.

2) academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
   - validates the selected UVA faculty identity against a trusted OpenAlex
     author mapping already present in the current roster/search view;
   - stages publication metadata in academiconline.Publication when the DOI
     does not already exist there;
   - creates ONE Pending candidate in FacultyPublicationReviewCandidate;
   - never creates a review decision and never creates a
     DepartmentRosterPublication relationship.

Important
---------
This procedure does NOT accept a publication for a faculty member.
Acceptance/rejection still occurs through the existing review workflow.

The Web layer for PATCH022C is intentionally Admin-only for this first
write-enabled milestone. Department-reviewer authorization can be widened only
after the write path is validated against the existing access-control service.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(
       N'academiconline.FacultyPublicationManualReviewSubmission',
       N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.FacultyPublicationManualReviewSubmission
    (
        FacultyPublicationManualReviewSubmissionId BIGINT
            IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FacultyPublicationManualReviewSubmission
            PRIMARY KEY,

        FacultyPublicationReviewCandidateId BIGINT NULL,
        PublicationKey BIGINT NULL,

        FacultyKey NVARCHAR(600) NOT NULL,
        DepartmentId BIGINT NOT NULL,
        UvaUserId NVARCHAR(512) NOT NULL,

        DoiNormalized NVARCHAR(512) NOT NULL,
        OpenAlexId NVARCHAR(128) NULL,
        OpenAlexAuthorId NVARCHAR(256) NULL,

        SubmissionStatus NVARCHAR(40) NOT NULL,
        SubmittedBy NVARCHAR(256) NOT NULL,
        SubmittedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_FacultyPublicationManualReviewSubmission_SubmittedAtUtc
            DEFAULT SYSUTCDATETIME(),

        ContextDataRunId BIGINT NULL
    );

    CREATE INDEX IX_FacultyPublicationManualReviewSubmission_DoiFaculty
        ON academiconline.FacultyPublicationManualReviewSubmission
        (
            DoiNormalized,
            FacultyKey,
            SubmittedAtUtc
        );

    CREATE INDEX IX_FacultyPublicationManualReviewSubmission_Candidate
        ON academiconline.FacultyPublicationManualReviewSubmission
        (
            FacultyPublicationReviewCandidateId,
            SubmittedAtUtc
        );
END;
GO

IF OBJECT_ID(
       N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi',
       N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE
        academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi;
END;
GO

CREATE PROCEDURE
    academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi
    @FacultyKey NVARCHAR(600),
    @DepartmentId BIGINT,
    @UvaUserId NVARCHAR(512),
    @OpenAlexAuthorId NVARCHAR(256),
    @OpenAlexWorkId NVARCHAR(128),
    @DoiNormalized NVARCHAR(512),
    @Title NVARCHAR(MAX),
    @Venue NVARCHAR(512) = NULL,
    @PublicationYear INT = NULL,
    @Link NVARCHAR(1024) = NULL,
    @SubmittedBy NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE
        @Now DATETIME2(7) = SYSUTCDATETIME(),
        @PublicationKey BIGINT,
        @CandidateId BIGINT,
        @WasCreated BIT = 0,
        @SubmissionStatus NVARCHAR(40),
        @ReviewSourceKey NVARCHAR(700),
        @DepartmentName NVARCHAR(512),
        @DivisionName NVARCHAR(512),
        @FacultyDisplayName NVARCHAR(512),
        @Email NVARCHAR(512),
        @Position NVARCHAR(512),
        @ContextDataRunId BIGINT;

    SELECT
        @FacultyKey = NULLIF(LTRIM(RTRIM(@FacultyKey)), N''),
        @UvaUserId = LOWER(NULLIF(LTRIM(RTRIM(@UvaUserId)), N'')),
        @OpenAlexAuthorId =
            UPPER(NULLIF(LTRIM(RTRIM(@OpenAlexAuthorId)), N'')),
        @OpenAlexWorkId =
            UPPER(NULLIF(LTRIM(RTRIM(@OpenAlexWorkId)), N'')),
        @DoiNormalized =
            LOWER(NULLIF(LTRIM(RTRIM(@DoiNormalized)), N'')),
        @Title = NULLIF(LTRIM(RTRIM(@Title)), N''),
        @Venue = NULLIF(LTRIM(RTRIM(@Venue)), N''),
        @Link = NULLIF(LTRIM(RTRIM(@Link)), N''),
        @SubmittedBy =
            NULLIF(LTRIM(RTRIM(@SubmittedBy)), N'');

    IF @FacultyKey IS NULL
       OR @DepartmentId IS NULL
       OR @UvaUserId IS NULL
       OR @OpenAlexAuthorId IS NULL
       OR @OpenAlexWorkId IS NULL
       OR @DoiNormalized IS NULL
       OR @Title IS NULL
       OR @SubmittedBy IS NULL
    BEGIN
        RAISERROR(
            'Faculty, department, trusted OpenAlex author/work, DOI, title, and submitter are required.',
            16,
            1);
        RETURN;
    END;

    IF @DoiNormalized NOT LIKE N'10.%'
    BEGIN
        RAISERROR('The DOI is not in the expected normalized form.', 16, 1);
        RETURN;
    END;

    IF @OpenAlexAuthorId NOT LIKE N'A%'
       OR @OpenAlexWorkId NOT LIKE N'W%'
    BEGIN
        RAISERROR(
            'The OpenAlex author/work identifiers are not in the expected form.',
            16,
            1);
        RETURN;
    END;

    /*
      Revalidate the exact faculty identity in the database.
      This mirrors the trusted OpenAlex rules used by Publication Freshness:
        - an OpenAlex publication identity with the same SourceAuthorId; OR
        - an accepted roster OpenAlex identity
          (ProposedUnique / DoiSeedConverged).
    */
    SELECT TOP (1)
        @DepartmentName = f.DepartmentName,
        @DivisionName = f.DivisionName,
        @FacultyDisplayName = f.DisplayName,
        @Email = f.Email,
        @Position = f.Position,
        @ContextDataRunId = f.DataRunId
    FROM academiconline.vw_FacultyPublicationSearch f
    OUTER APPLY
    (
        SELECT TOP (1)
            rm.OpenAlexAuthorId,
            rm.OpenAlexResolutionStatus
        FROM academiconline.DepartmentRosterMember rm
        WHERE rm.DepartmentId = f.DepartmentId
          AND rm.IsActive = 1
          AND
          (
              (
                  f.FacultyKey LIKE N'ROSTER:%'
                  AND rm.DepartmentRosterMemberId =
                      TRY_CONVERT(
                          BIGINT,
                          SUBSTRING(
                              f.FacultyKey,
                              LEN(N'ROSTER:') + 1,
                              50))
              )
              OR
              (
                  NULLIF(LTRIM(RTRIM(f.Email)), N'') IS NOT NULL
                  AND LOWER(LTRIM(RTRIM(rm.ExpectedEmail))) =
                      LOWER(LTRIM(RTRIM(f.Email)))
              )
              OR
              (
                  f.PublicationIdentitySource = N'AcademicOnline'
                  AND NULLIF(LTRIM(RTRIM(rm.AcademicOnlineAuthorId)), N'') =
                      NULLIF(LTRIM(RTRIM(f.SourceAuthorId)), N'')
              )
          )
        ORDER BY
            CASE
                WHEN f.FacultyKey LIKE N'ROSTER:%'
                 AND rm.DepartmentRosterMemberId =
                     TRY_CONVERT(
                         BIGINT,
                         SUBSTRING(
                             f.FacultyKey,
                             LEN(N'ROSTER:') + 1,
                             50))
                    THEN 0
                ELSE 1
            END,
            rm.DepartmentRosterMemberId
    ) roster
    WHERE f.FacultyKey = @FacultyKey
      AND f.DepartmentId = @DepartmentId
      AND LOWER(LTRIM(RTRIM(f.UvaUserId))) = @UvaUserId
      AND
      (
          (
              f.PublicationIdentitySource = N'OpenAlex'
              AND UPPER(LTRIM(RTRIM(f.SourceAuthorId))) =
                  @OpenAlexAuthorId
          )
          OR
          (
              roster.OpenAlexResolutionStatus IN
                  (N'ProposedUnique', N'DoiSeedConverged')
              AND UPPER(LTRIM(RTRIM(roster.OpenAlexAuthorId))) =
                  @OpenAlexAuthorId
          )
      )
    ORDER BY f.DataRunId DESC;

    IF @FacultyDisplayName IS NULL
    BEGIN
        RAISERROR(
            'The trusted UVA/OpenAlex faculty association could not be revalidated.',
            16,
            1);
        RETURN;
    END;

    /*
      The candidate table requires data-run IDs. A manual DOI lookup is not a
      source synchronization run, so we preserve the faculty identity's current
      DataRunId only as context. If none exists, zero is an explicit sentinel.
      The separate manual-submission audit table records the true provenance.
    */
    SET @ContextDataRunId = COALESCE(@ContextDataRunId, 0);

    BEGIN TRY
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        BEGIN TRANSACTION;

        /*
          Stage/fetch the canonical publication row.
          This does NOT create a DepartmentRosterPublication relationship.
        */
        SELECT TOP (1)
            @PublicationKey = p.PublicationKey
        FROM academiconline.Publication p WITH (UPDLOCK, HOLDLOCK)
        WHERE
            LOWER(LTRIM(RTRIM(COALESCE(p.DoiNormalized, p.Doi)))) =
                @DoiNormalized
            OR UPPER(LTRIM(RTRIM(p.OpenAlexId))) =
                @OpenAlexWorkId
        ORDER BY
            CASE
                WHEN LOWER(LTRIM(RTRIM(COALESCE(p.DoiNormalized, p.Doi)))) =
                     @DoiNormalized
                    THEN 0
                ELSE 1
            END,
            p.PublicationKey;

        IF @PublicationKey IS NULL
        BEGIN
            INSERT INTO academiconline.Publication
            (
                AcademicOnlinePublicationId,
                OpenAlexId,
                Doi,
                DoiNormalized,
                Title,
                TitleYearFingerprint,
                Venue,
                YearText,
                PublicationYear,
                CitationCount,
                Verified,
                Link,
                CoauthorsJson,
                KeywordsJson,
                RawJson,
                LastRetrievedAtUtc,
                LastSyncRunId
            )
            VALUES
            (
                NULL,
                @OpenAlexWorkId,
                @DoiNormalized,
                @DoiNormalized,
                @Title,
                NULL,
                @Venue,
                CASE
                    WHEN @PublicationYear IS NULL THEN NULL
                    ELSE CONVERT(NVARCHAR(50), @PublicationYear)
                END,
                @PublicationYear,
                NULL,
                NULL,
                @Link,
                NULL,
                NULL,
                NULL,
                @Now,
                NULL
            );

            SET @PublicationKey = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE academiconline.Publication
            SET
                OpenAlexId =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(OpenAlexId)), N''),
                        @OpenAlexWorkId),
                Doi =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(Doi)), N''),
                        @DoiNormalized),
                DoiNormalized =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(DoiNormalized)), N''),
                        @DoiNormalized),
                Title =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(Title)), N''),
                        @Title),
                Venue =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(Venue)), N''),
                        @Venue),
                YearText =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(YearText)), N''),
                        CASE
                            WHEN @PublicationYear IS NULL THEN NULL
                            ELSE CONVERT(NVARCHAR(50), @PublicationYear)
                        END),
                PublicationYear =
                    COALESCE(
                        PublicationYear,
                        @PublicationYear),
                Link =
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(Link)), N''),
                        @Link),
                LastRetrievedAtUtc = @Now
            WHERE PublicationKey = @PublicationKey;
        END;

        SELECT
            @CandidateId =
                c.FacultyPublicationReviewCandidateId
        FROM academiconline.FacultyPublicationReviewCandidate c
             WITH (UPDLOCK, HOLDLOCK)
        WHERE c.FacultyKey = @FacultyKey
          AND c.PublicationKey = @PublicationKey;

        IF @CandidateId IS NULL
        BEGIN
            SET @ReviewSourceKey =
                N'MANUALDOI:' +
                CONVERT(
                    NVARCHAR(64),
                    HASHBYTES(
                        'SHA2_256',
                        CONCAT(
                            @FacultyKey,
                            N'|',
                            CONVERT(NVARCHAR(30), @PublicationKey),
                            N'|',
                            @DoiNormalized)),
                    2);

            INSERT INTO
                academiconline.FacultyPublicationReviewCandidate
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
                FirstSeenDataRunId,
                LastSeenDataRunId,
                FirstSeenAtUtc,
                LastSeenAtUtc,
                IsCurrentSource,
                CreatedAtUtc,
                UpdatedAtUtc,
                IsInCurrentReportingWindow
            )
            VALUES
            (
                @ReviewSourceKey,
                @FacultyKey,
                @PublicationKey,
                @DepartmentId,
                @DepartmentName,
                @DivisionName,
                @UvaUserId,
                @FacultyDisplayName,
                @Email,
                @Position,
                NULL,
                N'OpenAlex',
                @OpenAlexAuthorId,
                N'ManualDoiLookup',
                N'OpenAlex',
                @OpenAlexAuthorId,
                NULL,
                @OpenAlexWorkId,
                @DoiNormalized,
                @DoiNormalized,
                @Title,
                @Venue,
                CASE
                    WHEN @PublicationYear IS NULL THEN NULL
                    ELSE CONVERT(NVARCHAR(50), @PublicationYear)
                END,
                @PublicationYear,
                NULL,
                NULL,
                @Link,
                @ContextDataRunId,
                @ContextDataRunId,
                @Now,
                @Now,
                1,
                @Now,
                @Now,
                1
            );

            SET @CandidateId = SCOPE_IDENTITY();
            SET @WasCreated = 1;
            SET @SubmissionStatus = N'CreatedPendingCandidate';
        END
        ELSE
        BEGIN
            SET @SubmissionStatus = N'AlreadyExists';
        END;

        INSERT INTO
            academiconline.FacultyPublicationManualReviewSubmission
        (
            FacultyPublicationReviewCandidateId,
            PublicationKey,
            FacultyKey,
            DepartmentId,
            UvaUserId,
            DoiNormalized,
            OpenAlexId,
            OpenAlexAuthorId,
            SubmissionStatus,
            SubmittedBy,
            SubmittedAtUtc,
            ContextDataRunId
        )
        VALUES
        (
            @CandidateId,
            @PublicationKey,
            @FacultyKey,
            @DepartmentId,
            @UvaUserId,
            @DoiNormalized,
            @OpenAlexWorkId,
            @OpenAlexAuthorId,
            @SubmissionStatus,
            @SubmittedBy,
            @Now,
            @ContextDataRunId
        );

        COMMIT TRANSACTION;

        SELECT
            FacultyPublicationReviewCandidateId = @CandidateId,
            PublicationKey = @PublicationKey,
            WasCreated = @WasCreated,
            SubmissionStatus = @SubmissionStatus;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH;
END;
GO

PRINT N'PATCH022C database objects installed.';
