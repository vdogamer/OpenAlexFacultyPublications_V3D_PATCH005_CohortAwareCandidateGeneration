:On Error exit
/*
PATCH022 review/auth prerequisite schema, schema-only snapshot supplied 2026-11-04.
NEW EMPTY DATABASE ONLY. Run via sqlcmd -b from the repository root.
This is a scoped review/auth baseline, NOT the entire OpenAlex ingestion database.
No source rows, users, role grants, passwords, principals or service configuration
are copied. The exported sync definition already includes fixes 450 and 457.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF DB_ID() <= 4
    THROW 51600, 'Never install the review/auth baseline into a system database.', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
   OR EXISTS (SELECT 1 FROM sys.views WHERE is_ms_shipped = 0)
   OR EXISTS (SELECT 1 FROM sys.procedures WHERE is_ms_shipped = 0)
   OR SCHEMA_ID(N'openalex') IS NOT NULL
   OR SCHEMA_ID(N'academiconline') IS NOT NULL
    THROW 51600, 'Baseline requires a new empty database without application schemas; no upgrade is attempted.', 1;

-- SQLCMD -b/:On Error exit closes the connection and rolls back uncommitted DDL on failure.
BEGIN TRANSACTION;
EXEC(N'CREATE SCHEMA openalex AUTHORIZATION dbo');
EXEC(N'CREATE SCHEMA academiconline AUTHORIZATION dbo');
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[DepartmentRosterMember](
	[DepartmentRosterMemberId] [bigint] IDENTITY(1,1) NOT NULL,
	[DepartmentId] [bigint] NOT NULL,
	[DisplayName] [nvarchar](512) NOT NULL,
	[ExpectedEmail] [nvarchar](512) NULL,
	[AcademicOnlineAuthorId] [nvarchar](128) NULL,
	[SearchPriority] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[SourceUrl] [nvarchar](1000) NULL,
	[SourceNote] [nvarchar](1000) NULL,
	[LastResolvedAtUtc] [datetime2](3) NULL,
	[ResolutionStatus] [nvarchar](30) NULL,
	[ResolutionError] [nvarchar](max) NULL,
	[CreatedAtUtc] [datetime2](3) NOT NULL,
	[UpdatedAtUtc] [datetime2](3) NOT NULL,
	[ResolutionMethod] [nvarchar](50) NULL,
	[ResolutionEvidence] [nvarchar](max) NULL,
	[OpenAlexAuthorId] [nvarchar](128) NULL,
	[OpenAlexResolutionStatus] [nvarchar](50) NULL,
	[OpenAlexResolutionEvidence] [nvarchar](max) NULL,
	[OpenAlexLastResolvedAtUtc] [datetime2](3) NULL,
	[OfficialUnitName] [nvarchar](512) NULL,
 CONSTRAINT [PK_academiconline_DepartmentRosterMember] PRIMARY KEY CLUSTERED
(
	[DepartmentRosterMemberId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_academiconline_DepartmentRosterMember] UNIQUE NONCLUSTERED
(
	[DepartmentId] ASC,
	[DisplayName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[SyncRun](
	[AcademicOnlineSyncRunId] [bigint] IDENTITY(1,1) NOT NULL,
	[SyncType] [nvarchar](50) NOT NULL,
	[DepartmentName] [nvarchar](512) NULL,
	[FromYear] [int] NULL,
	[ToYear] [int] NULL,
	[VerifiedOnly] [bit] NOT NULL,
	[RunStatus] [nvarchar](30) NOT NULL,
	[ScholarsProcessed] [int] NOT NULL,
	[PublicationsProcessed] [int] NOT NULL,
	[ScholarPagesFetched] [int] NOT NULL,
	[PublicationPagesFetched] [int] NOT NULL,
	[StartedBy] [nvarchar](256) NOT NULL,
	[ApplicationVersion] [nvarchar](128) NOT NULL,
	[SummaryText] [nvarchar](max) NULL,
	[ErrorText] [nvarchar](max) NULL,
	[StartedAtUtc] [datetime2](3) NOT NULL,
	[CompletedAtUtc] [datetime2](3) NULL,
	[CreatedAtUtc] [datetime2](3) NOT NULL,
	[UpdatedAtUtc] [datetime2](3) NOT NULL,
	[ScholarsDiscovered] [int] NULL,
	[MaxScholarsToProcess] [int] NULL,
	[ScholarPaginationSafeguardHit] [bit] NOT NULL,
	[PublicationPaginationSafeguardHitCount] [int] NOT NULL,
	[IsCacheComplete] [bit] NULL,
	[SnapshotPromoted] [bit] NOT NULL,
 CONSTRAINT [PK_academiconline_SyncRun] PRIMARY KEY CLUSTERED
(
	[AcademicOnlineSyncRunId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[Department](
	[DepartmentId] [bigint] IDENTITY(1,1) NOT NULL,
	[SourceName] [nvarchar](512) NOT NULL,
	[CanonicalName] [nvarchar](512) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[FirstSeenAtUtc] [datetime2](3) NOT NULL,
	[LastRetrievedAtUtc] [datetime2](3) NOT NULL,
	[LastSyncRunId] [bigint] NULL,
	[CurrentSnapshotRunId] [bigint] NULL,
	[CurrentSnapshotAtUtc] [datetime2](3) NULL,
	[RefreshEnabled] [bit] NOT NULL,
	[RefreshIntervalHours] [int] NOT NULL,
	[RefreshFailureRetryMinutes] [int] NOT NULL,
	[RefreshFromYear] [int] NULL,
	[RefreshToYear] [int] NULL,
	[RefreshVerifiedOnly] [bit] NOT NULL,
	[RefreshMaxScholars] [int] NOT NULL,
	[NextRefreshDueAtUtc] [datetime2](3) NULL,
	[LastRefreshAttemptAtUtc] [datetime2](3) NULL,
	[LastRefreshAttemptStatus] [nvarchar](30) NULL,
	[LastRefreshAttemptRunId] [bigint] NULL,
	[LastRefreshError] [nvarchar](max) NULL,
	[RefreshLeaseOwner] [nvarchar](256) NULL,
	[RefreshLeaseUntilUtc] [datetime2](3) NULL,
	[AcademicOnlineDepartmentId] [nvarchar](128) NULL,
	[AcademicOnlineSlug] [nvarchar](256) NULL,
	[ParentAcademicOnlineDepartmentId] [nvarchar](128) NULL,
	[ParentName] [nvarchar](512) NULL,
	[ParentSlug] [nvarchar](256) NULL,
	[DiscoveryScholarCount] [int] NULL,
	[DiscoveryFirstSeenAtUtc] [datetime2](3) NULL,
	[DiscoveryLastSeenAtUtc] [datetime2](3) NULL,
	[LastDiscoveryRunId] [bigint] NULL,
	[SourceMode] [nvarchar](30) NOT NULL,
	[SourceModeNote] [nvarchar](1000) NULL,
 CONSTRAINT [PK_academiconline_Department] PRIMARY KEY CLUSTERED
(
	[DepartmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_academiconline_Department_SourceName] UNIQUE NONCLUSTERED
(
	[SourceName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[Scholar](
	[AcademicOnlineAuthorId] [nvarchar](128) NOT NULL,
	[UvaUserId] [nvarchar](32) NULL,
	[DisplayName] [nvarchar](512) NOT NULL,
	[Email] [nvarchar](512) NULL,
	[Position] [nvarchar](512) NULL,
	[Institution] [nvarchar](512) NULL,
	[OrcidUrl] [nvarchar](512) NULL,
	[PublicationCount] [int] NULL,
	[HasPublications] [bit] NULL,
	[HasVerifiedPublications] [bit] NULL,
	[RawJson] [nvarchar](max) NULL,
	[FirstSeenAtUtc] [datetime2](3) NOT NULL,
	[LastRetrievedAtUtc] [datetime2](3) NOT NULL,
	[LastSyncRunId] [bigint] NULL,
 CONSTRAINT [PK_academiconline_Scholar] PRIMARY KEY CLUSTERED
(
	[AcademicOnlineAuthorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[ScholarDepartment](
	[AcademicOnlineAuthorId] [nvarchar](128) NOT NULL,
	[DepartmentId] [bigint] NOT NULL,
	[FirstSeenAtUtc] [datetime2](3) NOT NULL,
	[LastSeenAtUtc] [datetime2](3) NOT NULL,
	[LastSyncRunId] [bigint] NULL,
	[IsCurrent] [bit] NOT NULL,
	[CurrentSnapshotRunId] [bigint] NULL,
 CONSTRAINT [PK_academiconline_ScholarDepartment] PRIMARY KEY CLUSTERED
(
	[AcademicOnlineAuthorId] ASC,
	[DepartmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[DepartmentRosterOpenAlexAuthorAlias](
	[DepartmentRosterOpenAlexAuthorAliasId] [bigint] IDENTITY(1,1) NOT NULL,
	[DepartmentRosterMemberId] [bigint] NOT NULL,
	[OpenAlexAuthorId] [nvarchar](256) NOT NULL,
	[EvidenceOpenAlexWorkId] [nvarchar](256) NULL,
	[EvidenceDoi] [nvarchar](1024) NULL,
	[AliasPriority] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[SourceReference] [nvarchar](1024) NULL,
	[SourceNote] [nvarchar](2000) NULL,
	[CreatedAtUtc] [datetime2](3) NOT NULL,
	[UpdatedAtUtc] [datetime2](3) NOT NULL,
 CONSTRAINT [PK_academiconline_DepartmentRosterOpenAlexAuthorAlias] PRIMARY KEY CLUSTERED
(
	[DepartmentRosterOpenAlexAuthorAliasId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_academiconline_DepartmentRosterOpenAlexAuthorAlias_MemberAuthor] UNIQUE NONCLUSTERED
(
	[DepartmentRosterMemberId] ASC,
	[OpenAlexAuthorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
/* ============================================================
   PATCH021N
   Faculty search view - add split OpenAlex profile support
   ============================================================ */

CREATE   VIEW [academiconline].[vw_FacultyPublicationSearch]
AS

WITH DepartmentRuns AS
(
    SELECT
        d.DepartmentId,
        d.CanonicalName AS DepartmentName,
        d.SourceMode,
        d.CurrentSnapshotRunId,

        FallbackCompletedRunId =
        (
            SELECT MAX(sr.AcademicOnlineSyncRunId)
            FROM academiconline.SyncRun sr
            WHERE sr.DepartmentName = d.CanonicalName
              AND sr.RunStatus = N'Completed'
              AND sr.ScholarsDiscovered IS NOT NULL
              AND sr.ScholarsProcessed = sr.ScholarsDiscovered
        )

    FROM academiconline.Department d
    WHERE d.IsActive = 1
),

AcademicOnlineFaculty AS
(
    SELECT DISTINCT

        FacultyKey =
            CAST(
                N'AO:' +
                CONVERT(
                    nvarchar(256),
                    s.AcademicOnlineAuthorId)
                AS nvarchar(600)
            ),

        dr.DepartmentId,
        dr.DepartmentName,

        DivisionName =
            CAST(NULL AS nvarchar(512)),

        s.UvaUserId,
        s.DisplayName,

        Email =
            NULLIF(s.Email, N'NULL'),

        Position =
            NULLIF(s.Position, N'NULL'),

        Orcid =
            NULLIF(s.OrcidUrl, N'NULL'),

        AcademicOnlineAuthorId =
            s.AcademicOnlineAuthorId,

        OpenAlexAuthorId =
            CAST(NULL AS nvarchar(256)),

        PublicationIdentitySource =
            CAST(N'AcademicOnline' AS nvarchar(30)),

        SourceAuthorId =
            s.AcademicOnlineAuthorId,

        DataRunId =
            CASE
                WHEN dr.CurrentSnapshotRunId IS NOT NULL
                    THEN dr.CurrentSnapshotRunId
                ELSE dr.FallbackCompletedRunId
            END,

        DepartmentDataComplete =
            CONVERT
            (
                bit,
                CASE
                    WHEN dr.CurrentSnapshotRunId IS NOT NULL
                        THEN 1
                    ELSE 0
                END
            )

    FROM academiconline.Scholar s

    INNER JOIN academiconline.ScholarDepartment sd
        ON sd.AcademicOnlineAuthorId =
           s.AcademicOnlineAuthorId

    INNER JOIN DepartmentRuns dr
        ON dr.DepartmentId =
           sd.DepartmentId

    WHERE
        (
            dr.CurrentSnapshotRunId IS NOT NULL
            AND sd.IsCurrent = 1
            AND sd.CurrentSnapshotRunId =
                dr.CurrentSnapshotRunId
        )

        OR

        (
            dr.CurrentSnapshotRunId IS NULL
            AND dr.FallbackCompletedRunId IS NOT NULL
            AND sd.LastSyncRunId =
                dr.FallbackCompletedRunId
        )
),

RosterFallbackFaculty AS
(
    SELECT

        FacultyKey =
            CAST(
                N'ROSTER:' +
                CONVERT(
                    nvarchar(30),
                    rm.DepartmentRosterMemberId)
                AS nvarchar(600)
            ),

        d.DepartmentId,

        DepartmentName =
            d.CanonicalName,

        DivisionName =
            rm.OfficialUnitName,

        UvaUserId =
            CASE
                WHEN rm.ExpectedEmail LIKE N'%@virginia.edu'
                 AND CHARINDEX(N'@', rm.ExpectedEmail) > 1
                THEN LEFT(
                    rm.ExpectedEmail,
                    CHARINDEX(N'@', rm.ExpectedEmail) - 1)
                ELSE NULL
            END,

        DisplayName =
            rm.DisplayName,

        Email =
            rm.ExpectedEmail,

        Position =
            CAST(NULL AS nvarchar(512)),

        Orcid =
            CAST(NULL AS nvarchar(512)),

        AcademicOnlineAuthorId =
            CAST(NULL AS nvarchar(256)),

        /*
         * For split-profile faculty this is only an internal
         * representative author ID. Publication retrieval uses
         * DepartmentRosterMemberId, not this single value.
         */
        OpenAlexAuthorId =
            COALESCE(
                rm.OpenAlexAuthorId,
                alias.OpenAlexAuthorId),

        PublicationIdentitySource =
            CAST(N'OpenAlex' AS nvarchar(30)),

        SourceAuthorId =
            COALESCE(
                rm.OpenAlexAuthorId,
                alias.OpenAlexAuthorId),

        DataRunId =
            COALESCE(
                d.CurrentSnapshotRunId,
                d.LastRefreshAttemptRunId),

        DepartmentDataComplete =
            CONVERT
            (
                bit,
                CASE
                    WHEN d.CurrentSnapshotRunId IS NOT NULL
                        THEN 1
                    ELSE 0
                END
            )

    FROM academiconline.DepartmentRosterMember rm

    INNER JOIN academiconline.Department d
        ON d.DepartmentId =
           rm.DepartmentId

    OUTER APPLY
    (
        SELECT TOP (1)
            a.OpenAlexAuthorId
        FROM academiconline.DepartmentRosterOpenAlexAuthorAlias a
        WHERE a.DepartmentRosterMemberId =
              rm.DepartmentRosterMemberId
          AND a.IsActive = 1
        ORDER BY
            a.AliasPriority,
            a.DepartmentRosterOpenAlexAuthorAliasId
    ) alias

    WHERE rm.IsActive = 1
      AND d.IsActive = 1

      /* AO-backed members already appear above */
      AND rm.AcademicOnlineAuthorId IS NULL

      AND
      (
          /* Normal single-profile fallback */
          (
              rm.OpenAlexAuthorId IS NOT NULL
              AND rm.OpenAlexResolutionStatus IN
                  (
                      N'ProposedUnique',
                      N'DoiSeedConverged'
                  )
          )

          OR

          /* PATCH021N split-profile fallback */
          alias.OpenAlexAuthorId IS NOT NULL
      )
)

SELECT
    FacultyKey,
    DepartmentId,
    DepartmentName,
    DivisionName,
    UvaUserId,
    DisplayName,
    Email,
    Position,
    Orcid,
    AcademicOnlineAuthorId,
    OpenAlexAuthorId,
    PublicationIdentitySource,
    SourceAuthorId,
    DataRunId,
    DepartmentDataComplete
FROM AcademicOnlineFaculty

UNION ALL

SELECT
    FacultyKey,
    DepartmentId,
    DepartmentName,
    DivisionName,
    UvaUserId,
    DisplayName,
    Email,
    Position,
    Orcid,
    AcademicOnlineAuthorId,
    OpenAlexAuthorId,
    PublicationIdentitySource,
    SourceAuthorId,
    DataRunId,
    DepartmentDataComplete
FROM RosterFallbackFaculty;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[FacultyPublicationReviewCandidate](
	[FacultyPublicationReviewCandidateId] [bigint] IDENTITY(1,1) NOT NULL,
	[ReviewSourceKey] [nvarchar](700) NOT NULL,
	[FacultyKey] [nvarchar](600) NOT NULL,
	[PublicationKey] [bigint] NOT NULL,
	[DepartmentId] [bigint] NOT NULL,
	[DepartmentName] [nvarchar](512) NOT NULL,
	[DivisionName] [nvarchar](512) NULL,
	[UvaUserId] [nvarchar](512) NULL,
	[FacultyDisplayName] [nvarchar](512) NOT NULL,
	[Email] [nvarchar](512) NULL,
	[Position] [nvarchar](512) NULL,
	[Orcid] [nvarchar](512) NULL,
	[PublicationIdentitySource] [nvarchar](30) NULL,
	[SourceAuthorId] [nvarchar](256) NULL,
	[RelationshipSource] [nvarchar](60) NOT NULL,
	[RelationshipSourceProvider] [nvarchar](60) NULL,
	[RelationshipSourceAuthorId] [nvarchar](256) NULL,
	[AcademicOnlinePublicationId] [nvarchar](128) NULL,
	[OpenAlexId] [nvarchar](128) NULL,
	[Doi] [nvarchar](512) NULL,
	[DoiNormalized] [nvarchar](512) NULL,
	[Title] [nvarchar](max) NULL,
	[Venue] [nvarchar](512) NULL,
	[YearText] [nvarchar](50) NULL,
	[PublicationYear] [int] NULL,
	[CitationCount] [int] NULL,
	[Verified] [bit] NULL,
	[Link] [nvarchar](1024) NULL,
	[FirstSeenDataRunId] [bigint] NOT NULL,
	[LastSeenDataRunId] [bigint] NOT NULL,
	[FirstSeenAtUtc] [datetime2](7) NOT NULL,
	[LastSeenAtUtc] [datetime2](7) NOT NULL,
	[IsCurrentSource] [bit] NOT NULL,
	[CreatedAtUtc] [datetime2](7) NOT NULL,
	[UpdatedAtUtc] [datetime2](7) NOT NULL,
	[IsInCurrentReportingWindow] [bit] NOT NULL,
 CONSTRAINT [PK_FacultyPublicationReviewCandidate] PRIMARY KEY CLUSTERED
(
	[FacultyPublicationReviewCandidateId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[FacultyPublicationReviewDecision](
	[FacultyPublicationReviewDecisionId] [bigint] IDENTITY(1,1) NOT NULL,
	[FacultyPublicationReviewCandidateId] [bigint] NOT NULL,
	[DecisionStatus] [nvarchar](20) NOT NULL,
	[DecisionReason] [nvarchar](200) NULL,
	[DecisionSource] [nvarchar](100) NOT NULL,
	[ReviewerNotes] [nvarchar](max) NULL,
	[ReviewedBy] [nvarchar](256) NOT NULL,
	[ReviewedAtUtc] [datetime2](7) NOT NULL,
	[IsCurrent] [bit] NOT NULL,
	[CreatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_FacultyPublicationReviewDecision] PRIMARY KEY CLUSTERED
(
	[FacultyPublicationReviewDecisionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   VIEW
    [academiconline].[vw_FacultyPublicationReviewStatus]
AS

SELECT
    c.FacultyPublicationReviewCandidateId,

    c.ReviewSourceKey,

    c.FacultyKey,
    c.PublicationKey,

    c.DepartmentId,
    c.DepartmentName,
    c.DivisionName,

    c.UvaUserId,
    c.FacultyDisplayName,
    c.Email,

    c.Doi,
    c.DoiNormalized,

    c.Title,
    c.Venue,
    c.PublicationYear,

    c.RelationshipSource,
    c.RelationshipSourceProvider,
    c.RelationshipSourceAuthorId,

    c.FirstSeenDataRunId,
    c.LastSeenDataRunId,

    c.FirstSeenAtUtc,
    c.LastSeenAtUtc,

    c.IsCurrentSource,

    c.IsInCurrentReportingWindow,

    ReportingWindowStatus =
        CASE
            WHEN c.IsInCurrentReportingWindow = 1
                THEN N'Current'
            ELSE N'Historical'
        END,

    ReviewStatus =
        CASE
            WHEN d.DecisionStatus = N'Accepted'
                THEN N'Accepted'

            WHEN d.DecisionStatus = N'Rejected'
                THEN N'Rejected'

            WHEN c.IsCurrentSource = 1
                THEN N'Pending'

            ELSE N'No Longer Current'
        END,

    d.FacultyPublicationReviewDecisionId,

    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,

    d.ReviewerNotes,

    d.ReviewedBy,
    d.ReviewedAtUtc

FROM
    academiconline.FacultyPublicationReviewCandidate c

LEFT JOIN
    academiconline.FacultyPublicationReviewDecision d

    ON d.FacultyPublicationReviewCandidateId =
       c.FacultyPublicationReviewCandidateId

   AND d.IsCurrent = 1;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[DepartmentRosterPublication](
	[DepartmentId] [bigint] NOT NULL,
	[DepartmentRosterMemberId] [bigint] NOT NULL,
	[PublicationKey] [bigint] NOT NULL,
	[SourceProvider] [nvarchar](30) NOT NULL,
	[SourceAuthorId] [nvarchar](128) NOT NULL,
	[IsCurrent] [bit] NOT NULL,
	[FirstSeenAtUtc] [datetime2](3) NOT NULL,
	[LastSeenAtUtc] [datetime2](3) NOT NULL,
	[LastSyncRunId] [bigint] NULL,
	[CurrentSnapshotRunId] [bigint] NULL,
 CONSTRAINT [PK_academiconline_DepartmentRosterPublication] PRIMARY KEY CLUSTERED
(
	[DepartmentId] ASC,
	[DepartmentRosterMemberId] ASC,
	[PublicationKey] ASC,
	[SourceProvider] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[Publication](
	[PublicationKey] [bigint] IDENTITY(1,1) NOT NULL,
	[AcademicOnlinePublicationId] [nvarchar](128) NULL,
	[OpenAlexId] [nvarchar](128) NULL,
	[Doi] [nvarchar](512) NULL,
	[DoiNormalized] [nvarchar](512) NULL,
	[Title] [nvarchar](max) NULL,
	[TitleYearFingerprint] [nvarchar](900) NULL,
	[Venue] [nvarchar](512) NULL,
	[YearText] [nvarchar](50) NULL,
	[PublicationYear] [int] NULL,
	[CitationCount] [int] NULL,
	[Verified] [bit] NULL,
	[Link] [nvarchar](1024) NULL,
	[CoauthorsJson] [nvarchar](max) NULL,
	[KeywordsJson] [nvarchar](max) NULL,
	[RawJson] [nvarchar](max) NULL,
	[FirstSeenAtUtc] [datetime2](3) NOT NULL,
	[LastRetrievedAtUtc] [datetime2](3) NOT NULL,
	[LastSyncRunId] [bigint] NULL,
 CONSTRAINT [PK_academiconline_Publication] PRIMARY KEY CLUSTERED
(
	[PublicationKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[DepartmentScholarPublication](
	[DepartmentId] [bigint] NOT NULL,
	[AcademicOnlineAuthorId] [nvarchar](128) NOT NULL,
	[PublicationKey] [bigint] NOT NULL,
	[IsCurrent] [bit] NOT NULL,
	[FirstSeenAtUtc] [datetime2](3) NOT NULL,
	[LastSeenAtUtc] [datetime2](3) NOT NULL,
	[LastSyncRunId] [bigint] NULL,
	[CurrentSnapshotRunId] [bigint] NULL,
 CONSTRAINT [PK_academiconline_DepartmentScholarPublication] PRIMARY KEY CLUSTERED
(
	[DepartmentId] ASC,
	[AcademicOnlineAuthorId] ASC,
	[PublicationKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
/* ============================================================
   PATCH021U Step 6D.1
   Full Known Faculty-Publication Review Universe

   vw_FacultyPublicationReviewSource
       = current operational reporting window

   vw_FacultyPublicationReviewUniverse
       = all known associations for current faculty

   One row per FacultyKey + PublicationKey.
   ============================================================ */

CREATE   VIEW
    [academiconline].[vw_FacultyPublicationReviewUniverse]
AS

WITH RawSource AS
(
    /* --------------------------------------------------------
       Academic Online relationships
       -------------------------------------------------------- */
    SELECT
        ReviewSourceKey =
            CAST(
                CONCAT(
                    f.FacultyKey,
                    N'|',
                    CONVERT(nvarchar(30), dsp.PublicationKey)
                )
                AS nvarchar(1300)
            ),

        f.FacultyKey,
        f.DepartmentId,
        f.DepartmentName,
        f.DivisionName,

        f.UvaUserId,
        f.DisplayName AS FacultyDisplayName,
        f.Email,
        f.Position,
        f.Orcid,

        f.PublicationIdentitySource,
        f.SourceAuthorId,

        RelationshipSource =
            CAST(N'AcademicOnline' AS nvarchar(60)),

        RelationshipSourceProvider =
            CAST(N'AcademicOnline' AS nvarchar(60)),

        RelationshipSourceAuthorId =
            dsp.AcademicOnlineAuthorId,

        dsp.PublicationKey,

        p.AcademicOnlinePublicationId,
        p.OpenAlexId,

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

        RelationshipSnapshotRunId =
            dsp.CurrentSnapshotRunId,

        IsInCurrentReportingWindow =
            CONVERT(bit, dsp.IsCurrent),

        f.DepartmentDataComplete

    FROM academiconline.vw_FacultyPublicationSearch f

    JOIN academiconline.DepartmentScholarPublication dsp
        ON dsp.DepartmentId = f.DepartmentId
       AND dsp.AcademicOnlineAuthorId =
           f.AcademicOnlineAuthorId

    JOIN academiconline.Publication p
        ON p.PublicationKey =
           dsp.PublicationKey

    WHERE
        f.FacultyKey LIKE N'AO:%'
        AND f.DepartmentDataComplete = 1


    UNION ALL


    /* --------------------------------------------------------
       Roster / OpenAlex relationships
       -------------------------------------------------------- */
    SELECT
        ReviewSourceKey =
            CAST(
                CONCAT(
                    f.FacultyKey,
                    N'|',
                    CONVERT(nvarchar(30), drp.PublicationKey)
                )
                AS nvarchar(1300)
            ),

        f.FacultyKey,
        f.DepartmentId,
        f.DepartmentName,
        f.DivisionName,

        f.UvaUserId,
        f.DisplayName AS FacultyDisplayName,
        f.Email,
        f.Position,
        f.Orcid,

        f.PublicationIdentitySource,
        f.SourceAuthorId,

        RelationshipSource =
            CAST(N'Roster' AS nvarchar(60)),

        RelationshipSourceProvider =
            drp.SourceProvider,

        RelationshipSourceAuthorId =
            drp.SourceAuthorId,

        drp.PublicationKey,

        p.AcademicOnlinePublicationId,
        p.OpenAlexId,

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

        RelationshipSnapshotRunId =
            drp.CurrentSnapshotRunId,

        IsInCurrentReportingWindow =
            CONVERT(bit, drp.IsCurrent),

        f.DepartmentDataComplete

    FROM academiconline.vw_FacultyPublicationSearch f

    JOIN academiconline.DepartmentRosterPublication drp
        ON drp.DepartmentId =
           f.DepartmentId

       AND drp.DepartmentRosterMemberId =
           TRY_CONVERT(
               bigint,
               SUBSTRING(
                   f.FacultyKey,
                   LEN(N'ROSTER:') + 1,
                   50
               )
           )

    JOIN academiconline.Publication p
        ON p.PublicationKey =
           drp.PublicationKey

    WHERE
        f.FacultyKey LIKE N'ROSTER:%'
        AND f.DepartmentDataComplete = 1
),

RankedSource AS
(
    SELECT
        RawSource.*,

        ReviewRowNumber =
            ROW_NUMBER() OVER
            (
                PARTITION BY
                    FacultyKey,
                    PublicationKey

                ORDER BY
                    IsInCurrentReportingWindow DESC,
                    RelationshipSnapshotRunId DESC,
                    RelationshipSourceProvider,
                    RelationshipSourceAuthorId
            )

    FROM RawSource
)

SELECT
    ReviewSourceKey,

    FacultyKey,
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

    PublicationKey,

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

FROM RankedSource

WHERE ReviewRowNumber = 1;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [openalex].[AppUserDepartmentAccess](
	[AppUserDepartmentAccessId] [bigint] IDENTITY(1,1) NOT NULL,
	[AppUserId] [bigint] NOT NULL,
	[DepartmentId] [bigint] NOT NULL,
	[CanReview] [bit] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAtUtc] [datetime2](7) NOT NULL,
	[UpdatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_AppUserDepartmentAccess] PRIMARY KEY CLUSTERED
(
	[AppUserDepartmentAccessId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_AppUserDepartmentAccess_UserDepartment] UNIQUE NONCLUSTERED
(
	[AppUserId] ASC,
	[DepartmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [openalex].[AppUserDepartmentAccessAudit](
	[AppUserDepartmentAccessAuditId] [bigint] IDENTITY(1,1) NOT NULL,
	[TargetAppUserId] [bigint] NOT NULL,
	[TargetUserName] [nvarchar](256) NOT NULL,
	[DepartmentId] [bigint] NOT NULL,
	[DepartmentName] [nvarchar](512) NULL,
	[ChangedBy] [nvarchar](256) NOT NULL,
	[ChangeType] [nvarchar](50) NOT NULL,
	[OldCanReview] [bit] NULL,
	[NewCanReview] [bit] NULL,
	[OldIsActive] [bit] NULL,
	[NewIsActive] [bit] NULL,
	[ChangeReason] [nvarchar](1000) NULL,
	[CreatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_AppUserDepartmentAccessAudit] PRIMARY KEY CLUSTERED
(
	[AppUserDepartmentAccessAuditId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [academiconline].[FacultyPublicationManualReviewSubmission](
	[FacultyPublicationManualReviewSubmissionId] [bigint] IDENTITY(1,1) NOT NULL,
	[FacultyPublicationReviewCandidateId] [bigint] NULL,
	[PublicationKey] [bigint] NULL,
	[FacultyKey] [nvarchar](600) NOT NULL,
	[DepartmentId] [bigint] NOT NULL,
	[UvaUserId] [nvarchar](512) NOT NULL,
	[DoiNormalized] [nvarchar](512) NOT NULL,
	[OpenAlexId] [nvarchar](128) NULL,
	[OpenAlexAuthorId] [nvarchar](256) NULL,
	[SubmissionStatus] [nvarchar](40) NOT NULL,
	[SubmittedBy] [nvarchar](256) NOT NULL,
	[SubmittedAtUtc] [datetime2](7) NOT NULL,
	[ContextDataRunId] [bigint] NULL,
 CONSTRAINT [PK_FacultyPublicationManualReviewSubmission] PRIMARY KEY CLUSTERED
(
	[FacultyPublicationManualReviewSubmissionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
SET ANSI_PADDING ON
GO
CREATE TABLE [openalex].[AppUser](
	[AppUserId] [bigint] IDENTITY(1,1) NOT NULL,
	[UserName] [nvarchar](256) NOT NULL,
	[DisplayName] [nvarchar](256) NOT NULL,
	[Email] [nvarchar](512) NULL,
	[SourceSystem] [varchar](100) NOT NULL,
	[IsEnabled] [bit] NOT NULL,
	[LastSeenAtUtc] [datetime2](7) NULL,
	[CreatedAtUtc] [datetime2](7) NOT NULL,
	[UpdatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_AppUser] PRIMARY KEY CLUSTERED
(
	[AppUserId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_AppUser_UserName] UNIQUE NONCLUSTERED
(
	[UserName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_PADDING OFF
GO
CREATE NONCLUSTERED INDEX [IX_FacultyPublicationReviewCandidate_Current_Department] ON [academiconline].[FacultyPublicationReviewCandidate]
(
	[IsCurrentSource] ASC,
	[DepartmentId] ASC
)
INCLUDE([UvaUserId],[FacultyDisplayName],[PublicationKey],[DoiNormalized],[PublicationYear]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_FacultyPublicationReviewCandidate_Faculty_Publication] ON [academiconline].[FacultyPublicationReviewCandidate]
(
	[FacultyKey] ASC,
	[PublicationKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AppUserDepartmentAccessAudit_UserCreated] ON [openalex].[AppUserDepartmentAccessAudit]
(
	[TargetAppUserId] ASC,
	[CreatedAtUtc] DESC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_FacultyPublicationManualReviewSubmission_Candidate] ON [academiconline].[FacultyPublicationManualReviewSubmission]
(
	[FacultyPublicationReviewCandidateId] ASC,
	[SubmittedAtUtc] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_FacultyPublicationManualReviewSubmission_DoiFaculty] ON [academiconline].[FacultyPublicationManualReviewSubmission]
(
	[DoiNormalized] ASC,
	[FacultyKey] ASC,
	[SubmittedAtUtc] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_academiconline_Department_AcademicOnlineId] ON [academiconline].[Department]
(
	[AcademicOnlineDepartmentId] ASC
)
WHERE ([AcademicOnlineDepartmentId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_academiconline_Department_AcademicOnlineSlug] ON [academiconline].[Department]
(
	[AcademicOnlineSlug] ASC
)
WHERE ([AcademicOnlineSlug] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Department_RefreshDue] ON [academiconline].[Department]
(
	[RefreshEnabled] ASC,
	[NextRefreshDueAtUtc] ASC,
	[RefreshLeaseUntilUtc] ASC,
	[DepartmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Scholar_Email] ON [academiconline].[Scholar]
(
	[Email] ASC
)
WHERE ([Email] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Scholar_UvaUserId] ON [academiconline].[Scholar]
(
	[UvaUserId] ASC
)
WHERE ([UvaUserId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Publication_AcademicOnlineId] ON [academiconline].[Publication]
(
	[AcademicOnlinePublicationId] ASC
)
WHERE ([AcademicOnlinePublicationId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Publication_DoiNormalized] ON [academiconline].[Publication]
(
	[DoiNormalized] ASC
)
WHERE ([DoiNormalized] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Publication_OpenAlexId] ON [academiconline].[Publication]
(
	[OpenAlexId] ASC
)
WHERE ([OpenAlexId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_Publication_Year] ON [academiconline].[Publication]
(
	[PublicationYear] DESC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_AppUser_Email] ON [openalex].[AppUser]
(
	[Email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_SyncRun_StatusStarted] ON [academiconline].[SyncRun]
(
	[RunStatus] ASC,
	[StartedAtUtc] DESC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_ScholarDepartment_Department] ON [academiconline].[ScholarDepartment]
(
	[DepartmentId] ASC,
	[AcademicOnlineAuthorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_DepartmentRosterMember_Author] ON [academiconline].[DepartmentRosterMember]
(
	[AcademicOnlineAuthorId] ASC
)
WHERE ([AcademicOnlineAuthorId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_DepartmentRosterMember_Department] ON [academiconline].[DepartmentRosterMember]
(
	[DepartmentId] ASC,
	[IsActive] ASC,
	[SearchPriority] ASC,
	[DisplayName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_FacultyPublicationReviewDecision_History] ON [academiconline].[FacultyPublicationReviewDecision]
(
	[FacultyPublicationReviewCandidateId] ASC,
	[ReviewedAtUtc] DESC
)
INCLUDE([DecisionStatus],[DecisionReason],[DecisionSource],[ReviewedBy],[IsCurrent]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_FacultyPublicationReviewDecision_Current] ON [academiconline].[FacultyPublicationReviewDecision]
(
	[FacultyPublicationReviewCandidateId] ASC
)
WHERE ([IsCurrent]=(1))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_DepartmentScholarPublication_Current] ON [academiconline].[DepartmentScholarPublication]
(
	[DepartmentId] ASC,
	[IsCurrent] ASC,
	[PublicationKey] ASC,
	[AcademicOnlineAuthorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_DepartmentRosterPublication_Current] ON [academiconline].[DepartmentRosterPublication]
(
	[DepartmentId] ASC,
	[IsCurrent] ASC,
	[CurrentSnapshotRunId] ASC,
	[PublicationKey] ASC,
	[DepartmentRosterMemberId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_DepartmentRosterPublication_Run] ON [academiconline].[DepartmentRosterPublication]
(
	[DepartmentId] ASC,
	[LastSyncRunId] ASC,
	[SourceProvider] ASC,
	[DepartmentRosterMemberId] ASC,
	[PublicationKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_academiconline_DepartmentRosterOpenAlexAuthorAlias_ActiveMember] ON [academiconline].[DepartmentRosterOpenAlexAuthorAlias]
(
	[DepartmentRosterMemberId] ASC,
	[IsActive] ASC,
	[AliasPriority] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewCandidate] ADD  CONSTRAINT [DF_FacultyPublicationReviewCandidate_FirstSeenAtUtc]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewCandidate] ADD  CONSTRAINT [DF_FacultyPublicationReviewCandidate_LastSeenAtUtc]  DEFAULT (sysutcdatetime()) FOR [LastSeenAtUtc]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewCandidate] ADD  CONSTRAINT [DF_FacultyPublicationReviewCandidate_IsCurrentSource]  DEFAULT ((1)) FOR [IsCurrentSource]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewCandidate] ADD  CONSTRAINT [DF_FacultyPublicationReviewCandidate_CreatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewCandidate] ADD  CONSTRAINT [DF_FacultyPublicationReviewCandidate_UpdatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [UpdatedAtUtc]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewCandidate] ADD  CONSTRAINT [DF_FacultyPublicationReviewCandidate_IsInCurrentReportingWindow]  DEFAULT ((0)) FOR [IsInCurrentReportingWindow]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccess] ADD  CONSTRAINT [DF_AppUserDepartmentAccess_CanReview]  DEFAULT ((1)) FOR [CanReview]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccess] ADD  CONSTRAINT [DF_AppUserDepartmentAccess_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccess] ADD  CONSTRAINT [DF_AppUserDepartmentAccess_CreatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccess] ADD  CONSTRAINT [DF_AppUserDepartmentAccess_UpdatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [UpdatedAtUtc]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccessAudit] ADD  CONSTRAINT [DF_AppUserDepartmentAccessAudit_CreatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [academiconline].[FacultyPublicationManualReviewSubmission] ADD  CONSTRAINT [DF_FacultyPublicationManualReviewSubmission_SubmittedAtUtc]  DEFAULT (sysutcdatetime()) FOR [SubmittedAtUtc]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_FirstSeen]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_RefreshEnabled]  DEFAULT ((0)) FOR [RefreshEnabled]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_RefreshIntervalHours]  DEFAULT ((168)) FOR [RefreshIntervalHours]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_RefreshFailureRetryMinutes]  DEFAULT ((60)) FOR [RefreshFailureRetryMinutes]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_RefreshVerifiedOnly]  DEFAULT ((0)) FOR [RefreshVerifiedOnly]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_RefreshMaxScholars]  DEFAULT ((250)) FOR [RefreshMaxScholars]
GO
ALTER TABLE [academiconline].[Department] ADD  CONSTRAINT [DF_academiconline_Department_SourceMode]  DEFAULT (N'DirectDepartment') FOR [SourceMode]
GO
ALTER TABLE [academiconline].[Scholar] ADD  CONSTRAINT [DF_academiconline_Scholar_FirstSeen]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [academiconline].[Publication] ADD  CONSTRAINT [DF_academiconline_Publication_FirstSeen]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [openalex].[AppUser] ADD  CONSTRAINT [DF_AppUser_SourceSystem]  DEFAULT ('Unknown') FOR [SourceSystem]
GO
ALTER TABLE [openalex].[AppUser] ADD  CONSTRAINT [DF_AppUser_IsEnabled]  DEFAULT ((1)) FOR [IsEnabled]
GO
ALTER TABLE [openalex].[AppUser] ADD  CONSTRAINT [DF_AppUser_CreatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [openalex].[AppUser] ADD  CONSTRAINT [DF_AppUser_UpdatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [UpdatedAtUtc]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_VerifiedOnly]  DEFAULT ((0)) FOR [VerifiedOnly]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_Scholars]  DEFAULT ((0)) FOR [ScholarsProcessed]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_Publications]  DEFAULT ((0)) FOR [PublicationsProcessed]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_ScholarPages]  DEFAULT ((0)) FOR [ScholarPagesFetched]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_PublicationPages]  DEFAULT ((0)) FOR [PublicationPagesFetched]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_Started]  DEFAULT (sysutcdatetime()) FOR [StartedAtUtc]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_Created]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_Updated]  DEFAULT (sysutcdatetime()) FOR [UpdatedAtUtc]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_ScholarPaginationSafeguardHit]  DEFAULT ((0)) FOR [ScholarPaginationSafeguardHit]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_PublicationPaginationSafeguardHitCount]  DEFAULT ((0)) FOR [PublicationPaginationSafeguardHitCount]
GO
ALTER TABLE [academiconline].[SyncRun] ADD  CONSTRAINT [DF_academiconline_SyncRun_SnapshotPromoted]  DEFAULT ((0)) FOR [SnapshotPromoted]
GO
ALTER TABLE [academiconline].[ScholarDepartment] ADD  CONSTRAINT [DF_academiconline_ScholarDepartment_FirstSeen]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [academiconline].[ScholarDepartment] ADD  CONSTRAINT [DF_academiconline_ScholarDepartment_IsCurrent]  DEFAULT ((0)) FOR [IsCurrent]
GO
ALTER TABLE [academiconline].[DepartmentRosterMember] ADD  CONSTRAINT [DF_academiconline_DepartmentRosterMember_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [academiconline].[DepartmentRosterMember] ADD  CONSTRAINT [DF_academiconline_DepartmentRosterMember_Created]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [academiconline].[DepartmentRosterMember] ADD  CONSTRAINT [DF_academiconline_DepartmentRosterMember_Updated]  DEFAULT (sysutcdatetime()) FOR [UpdatedAtUtc]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewDecision] ADD  CONSTRAINT [DF_FacultyPublicationReviewDecision_IsCurrent]  DEFAULT ((1)) FOR [IsCurrent]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewDecision] ADD  CONSTRAINT [DF_FacultyPublicationReviewDecision_CreatedAtUtc]  DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication] ADD  CONSTRAINT [DF_academiconline_DepartmentScholarPublication_IsCurrent]  DEFAULT ((0)) FOR [IsCurrent]
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication] ADD  CONSTRAINT [DF_academiconline_DepartmentScholarPublication_FirstSeen]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication] ADD  CONSTRAINT [DF_academiconline_DepartmentRosterPublication_IsCurrent]  DEFAULT ((0)) FOR [IsCurrent]
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication] ADD  CONSTRAINT [DF_academiconline_DepartmentRosterPublication_FirstSeen]  DEFAULT (sysutcdatetime()) FOR [FirstSeenAtUtc]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccess]  WITH CHECK ADD  CONSTRAINT [FK_AppUserDepartmentAccess_AppUser] FOREIGN KEY([AppUserId])
REFERENCES [openalex].[AppUser] ([AppUserId])
GO
ALTER TABLE [openalex].[AppUserDepartmentAccess] CHECK CONSTRAINT [FK_AppUserDepartmentAccess_AppUser]
GO
ALTER TABLE [openalex].[AppUserDepartmentAccessAudit]  WITH CHECK ADD  CONSTRAINT [FK_AppUserDepartmentAccessAudit_AppUser] FOREIGN KEY([TargetAppUserId])
REFERENCES [openalex].[AppUser] ([AppUserId])
GO
ALTER TABLE [openalex].[AppUserDepartmentAccessAudit] CHECK CONSTRAINT [FK_AppUserDepartmentAccessAudit_AppUser]
GO
ALTER TABLE [academiconline].[ScholarDepartment]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_ScholarDepartment_Department] FOREIGN KEY([DepartmentId])
REFERENCES [academiconline].[Department] ([DepartmentId])
GO
ALTER TABLE [academiconline].[ScholarDepartment] CHECK CONSTRAINT [FK_academiconline_ScholarDepartment_Department]
GO
ALTER TABLE [academiconline].[ScholarDepartment]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_ScholarDepartment_Scholar] FOREIGN KEY([AcademicOnlineAuthorId])
REFERENCES [academiconline].[Scholar] ([AcademicOnlineAuthorId])
GO
ALTER TABLE [academiconline].[ScholarDepartment] CHECK CONSTRAINT [FK_academiconline_ScholarDepartment_Scholar]
GO
ALTER TABLE [academiconline].[DepartmentRosterMember]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentRosterMember_Department] FOREIGN KEY([DepartmentId])
REFERENCES [academiconline].[Department] ([DepartmentId])
GO
ALTER TABLE [academiconline].[DepartmentRosterMember] CHECK CONSTRAINT [FK_academiconline_DepartmentRosterMember_Department]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewDecision]  WITH CHECK ADD  CONSTRAINT [FK_FacultyPublicationReviewDecision_Candidate] FOREIGN KEY([FacultyPublicationReviewCandidateId])
REFERENCES [academiconline].[FacultyPublicationReviewCandidate] ([FacultyPublicationReviewCandidateId])
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewDecision] CHECK CONSTRAINT [FK_FacultyPublicationReviewDecision_Candidate]
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentScholarPublication_Department] FOREIGN KEY([DepartmentId])
REFERENCES [academiconline].[Department] ([DepartmentId])
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication] CHECK CONSTRAINT [FK_academiconline_DepartmentScholarPublication_Department]
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentScholarPublication_Publication] FOREIGN KEY([PublicationKey])
REFERENCES [academiconline].[Publication] ([PublicationKey])
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication] CHECK CONSTRAINT [FK_academiconline_DepartmentScholarPublication_Publication]
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentScholarPublication_Scholar] FOREIGN KEY([AcademicOnlineAuthorId])
REFERENCES [academiconline].[Scholar] ([AcademicOnlineAuthorId])
GO
ALTER TABLE [academiconline].[DepartmentScholarPublication] CHECK CONSTRAINT [FK_academiconline_DepartmentScholarPublication_Scholar]
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentRosterPublication_Department] FOREIGN KEY([DepartmentId])
REFERENCES [academiconline].[Department] ([DepartmentId])
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication] CHECK CONSTRAINT [FK_academiconline_DepartmentRosterPublication_Department]
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentRosterPublication_Publication] FOREIGN KEY([PublicationKey])
REFERENCES [academiconline].[Publication] ([PublicationKey])
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication] CHECK CONSTRAINT [FK_academiconline_DepartmentRosterPublication_Publication]
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentRosterPublication_RosterMember] FOREIGN KEY([DepartmentRosterMemberId])
REFERENCES [academiconline].[DepartmentRosterMember] ([DepartmentRosterMemberId])
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication] CHECK CONSTRAINT [FK_academiconline_DepartmentRosterPublication_RosterMember]
GO
ALTER TABLE [academiconline].[DepartmentRosterOpenAlexAuthorAlias]  WITH CHECK ADD  CONSTRAINT [FK_academiconline_DepartmentRosterOpenAlexAuthorAlias_RosterMember] FOREIGN KEY([DepartmentRosterMemberId])
REFERENCES [academiconline].[DepartmentRosterMember] ([DepartmentRosterMemberId])
GO
ALTER TABLE [academiconline].[DepartmentRosterOpenAlexAuthorAlias] CHECK CONSTRAINT [FK_academiconline_DepartmentRosterOpenAlexAuthorAlias_RosterMember]
GO
ALTER TABLE [academiconline].[Department]  WITH CHECK ADD  CONSTRAINT [CK_academiconline_Department_SourceMode] CHECK  (([SourceMode]=N'FacultyRoster' OR [SourceMode]=N'AggregateUnits' OR [SourceMode]=N'DirectDepartment'))
GO
ALTER TABLE [academiconline].[Department] CHECK CONSTRAINT [CK_academiconline_Department_SourceMode]
GO
ALTER TABLE [academiconline].[Publication]  WITH CHECK ADD  CONSTRAINT [CK_academiconline_Publication_CitationCount] CHECK  (([CitationCount] IS NULL OR [CitationCount]>=(0)))
GO
ALTER TABLE [academiconline].[Publication] CHECK CONSTRAINT [CK_academiconline_Publication_CitationCount]
GO
ALTER TABLE [academiconline].[Publication]  WITH CHECK ADD  CONSTRAINT [CK_academiconline_Publication_PublicationYear] CHECK  (([PublicationYear] IS NULL OR [PublicationYear]>=(1000) AND [PublicationYear]<=(2100)))
GO
ALTER TABLE [academiconline].[Publication] CHECK CONSTRAINT [CK_academiconline_Publication_PublicationYear]
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewDecision]  WITH CHECK ADD  CONSTRAINT [CK_FacultyPublicationReviewDecision_Status] CHECK  (([DecisionStatus]=N'Rejected' OR [DecisionStatus]=N'Accepted'))
GO
ALTER TABLE [academiconline].[FacultyPublicationReviewDecision] CHECK CONSTRAINT [CK_FacultyPublicationReviewDecision_Status]
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication]  WITH CHECK ADD  CONSTRAINT [CK_academiconline_DepartmentRosterPublication_SourceProvider] CHECK  (([SourceProvider]=N'OpenAlex' OR [SourceProvider]=N'AcademicOnline'))
GO
ALTER TABLE [academiconline].[DepartmentRosterPublication] CHECK CONSTRAINT [CK_academiconline_DepartmentRosterPublication_SourceProvider]
GO
ALTER TABLE [academiconline].[DepartmentRosterOpenAlexAuthorAlias]  WITH CHECK ADD  CONSTRAINT [CK_academiconline_DepartmentRosterOpenAlexAuthorAlias_Priority] CHECK  (([AliasPriority]>(0)))
GO
ALTER TABLE [academiconline].[DepartmentRosterOpenAlexAuthorAlias] CHECK CONSTRAINT [CK_academiconline_DepartmentRosterOpenAlexAuthorAlias_Priority]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
/* ============================================================
   PATCH021T
   Step 5 - Record Faculty Publication Review Decision

   Supports:
       Accepted
       Rejected

   Preserves prior decisions as history.
   Only one current decision is permitted per candidate.
   ============================================================ */

CREATE   PROCEDURE
    [academiconline].[usp_SetFacultyPublicationReviewDecision]
(
    @FacultyPublicationReviewCandidateId BIGINT,
    @DecisionStatus NVARCHAR(20),
    @ReviewedBy NVARCHAR(256),
    @DecisionReason NVARCHAR(200) = NULL,
    @DecisionSource NVARCHAR(100) = N'HumanReviewer',
    @ReviewerNotes NVARCHAR(MAX) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ------------------------------------------------------------
    -- Validate decision
    ------------------------------------------------------------
    IF @DecisionStatus NOT IN
    (
        N'Accepted',
        N'Rejected'
    )
    BEGIN
        THROW 51010,
            'DecisionStatus must be Accepted or Rejected.',
            1;
    END;

    IF NULLIF(LTRIM(RTRIM(@ReviewedBy)), N'') IS NULL
    BEGIN
        THROW 51011,
            'ReviewedBy is required.',
            1;
    END;

    ------------------------------------------------------------
    -- Candidate must exist and still belong to current source
    ------------------------------------------------------------
    IF NOT EXISTS
    (
        SELECT 1
        FROM academiconline.FacultyPublicationReviewCandidate
        WHERE FacultyPublicationReviewCandidateId =
              @FacultyPublicationReviewCandidateId
          AND IsCurrentSource = 1
    )
    BEGIN
        THROW 51012,
            'The review candidate does not exist or is no longer current.',
            1;
    END;

    BEGIN TRANSACTION;

    ------------------------------------------------------------
    -- Retire prior current decision, if any
    ------------------------------------------------------------
    UPDATE academiconline.FacultyPublicationReviewDecision
    SET
        IsCurrent = 0
    WHERE FacultyPublicationReviewCandidateId =
          @FacultyPublicationReviewCandidateId
      AND IsCurrent = 1;

    ------------------------------------------------------------
    -- Create new current decision
    ------------------------------------------------------------
    INSERT INTO academiconline.FacultyPublicationReviewDecision
    (
        FacultyPublicationReviewCandidateId,
        DecisionStatus,
        DecisionReason,
        DecisionSource,
        ReviewerNotes,
        ReviewedBy,
        ReviewedAtUtc,
        IsCurrent,
        CreatedAtUtc
    )
    VALUES
    (
        @FacultyPublicationReviewCandidateId,
        @DecisionStatus,
        NULLIF(LTRIM(RTRIM(@DecisionReason)), N''),
        COALESCE(
            NULLIF(LTRIM(RTRIM(@DecisionSource)), N''),
            N'HumanReviewer'
        ),
        NULLIF(LTRIM(RTRIM(@ReviewerNotes)), N''),
        LTRIM(RTRIM(@ReviewedBy)),
        SYSUTCDATETIME(),
        1,
        SYSUTCDATETIME()
    );

    COMMIT TRANSACTION;

    ------------------------------------------------------------
    -- Return resulting state
    ------------------------------------------------------------
    SELECT
        FacultyPublicationReviewCandidateId,
        FacultyKey,
        UvaUserId,
        FacultyDisplayName,
        PublicationKey,
        DoiNormalized,
        Title,
        ReviewStatus,
        DecisionStatus,
        DecisionReason,
        DecisionSource,
        ReviewerNotes,
        ReviewedBy,
        ReviewedAtUtc
    FROM academiconline.vw_FacultyPublicationReviewStatus
    WHERE FacultyPublicationReviewCandidateId =
          @FacultyPublicationReviewCandidateId;
END;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE
    [academiconline].[usp_SyncFacultyPublicationReviewCandidatesForDepartment]
(
    @DepartmentId BIGINT
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE
        @SourceRows BIGINT = 0,
        @UpdatedRows INT = 0,
        @InsertedRows INT = 0,
        @RetiredRows INT = 0;


    IF @DepartmentId IS NULL
    BEGIN
        THROW 51100,
            'DepartmentId is required.',
            1;
    END;


    IF OBJECT_ID(
        'tempdb..#CurrentReviewSource'
    ) IS NOT NULL
    BEGIN
        DROP TABLE #CurrentReviewSource;
    END;


    SELECT
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

    INTO #CurrentReviewSource

    FROM
        academiconline.vw_FacultyPublicationReviewUniverse

    WHERE DepartmentId =
          @DepartmentId;


    SELECT
        @SourceRows = COUNT_BIG(*)

    FROM #CurrentReviewSource;


    /* ManualDoiReviewLifecycle_v1_BEGIN */
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
        CONVERT(nvarchar(30), N'OpenAlex') AS PublicationIdentitySource,
        a.OpenAlexAuthorId AS SourceAuthorId,
        CONVERT(nvarchar(60), N'ManualDoiLookup') AS RelationshipSource,
        CONVERT(nvarchar(60), N'OpenAlex') AS RelationshipSourceProvider,
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
          AND s.SubmissionStatus = N'CreatedPendingCandidate'
          AND NULLIF(LTRIM(RTRIM(s.OpenAlexAuthorId)), N'') IS NOT NULL
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
             AND sr.RunStatus = N'Completed'
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
                   AND (f.FacultyKey LIKE N'AO:_%'
                        OR (f.FacultyKey LIKE N'ROSTER:%'
                            AND TRY_CONVERT(bigint, SUBSTRING(
                                f.FacultyKey, LEN(N'ROSTER:') + 1, 50)) IS NOT NULL))
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
        THROW 51101, 'Review synchronization aborted: automatic source is unexpectedly empty; manual candidates do not bypass source safety checks.', 1;
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
    IF @SourceRows = 0
    BEGIN

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
              AND sr.RunStatus = N'Completed'
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
                    AND (f.FacultyKey LIKE N'AO:_%'
                         OR (f.FacultyKey LIKE N'ROSTER:%'
                             AND TRY_CONVERT(bigint, SUBSTRING(
                                 f.FacultyKey, LEN(N'ROSTER:') + 1, 50)) IS NOT NULL))
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
            PRINT N'NoCandidates: complete promoted snapshot has zero qualifying publications and no prior review associations.';
            SELECT
                DepartmentId = @DepartmentId,
                SourceRows = @SourceRows,
                UpdatedRows = @UpdatedRows,
                InsertedRows = @InsertedRows,
                RetiredRows = @RetiredRows,
                CurrentCandidateRows = CONVERT(bigint, 0);
            RETURN;
        END;

        THROW 51101,
            'Review synchronization aborted: no review-universe rows exist for this department.',
            1;
    END;


    IF EXISTS
    (
        SELECT
            FacultyKey,
            PublicationKey

        FROM #CurrentReviewSource

        GROUP BY
            FacultyKey,
            PublicationKey

        HAVING COUNT_BIG(*) > 1
    )
    BEGIN
        THROW 51102,
            'Review synchronization aborted: duplicate FacultyKey + PublicationKey rows exist.',
            1;
    END;


    BEGIN TRANSACTION;


    /* --------------------------------------------------------
       Update existing candidates
       -------------------------------------------------------- */

    UPDATE c
    SET
        c.ReviewSourceKey =
            s.ReviewSourceKey,

        c.DepartmentId =
            s.DepartmentId,

        c.DepartmentName =
            LEFT(s.DepartmentName, 512),

        c.DivisionName =
            LEFT(s.DivisionName, 512),

        c.UvaUserId =
            LEFT(s.UvaUserId, 512),

        c.FacultyDisplayName =
            LEFT(s.FacultyDisplayName, 512),

        c.Email =
            LEFT(s.Email, 512),

        c.Position =
            LEFT(s.Position, 512),

        c.Orcid =
            LEFT(s.Orcid, 512),

        c.PublicationIdentitySource =
            LEFT(s.PublicationIdentitySource, 30),

        c.SourceAuthorId =
            LEFT(s.SourceAuthorId, 256),

        c.RelationshipSource =
            LEFT(s.RelationshipSource, 60),

        c.RelationshipSourceProvider =
            LEFT(s.RelationshipSourceProvider, 60),

        c.RelationshipSourceAuthorId =
            LEFT(s.RelationshipSourceAuthorId, 256),

        c.AcademicOnlinePublicationId =
            LEFT(s.AcademicOnlinePublicationId, 128),

        c.OpenAlexId =
            LEFT(s.OpenAlexId, 128),

        c.Doi =
            LEFT(s.Doi, 512),

        c.DoiNormalized =
            LEFT(s.DoiNormalized, 512),

        c.Title =
            s.Title,

        c.Venue =
            LEFT(s.Venue, 512),

        c.YearText =
            LEFT(s.YearText, 50),

        c.PublicationYear =
            s.PublicationYear,

        c.CitationCount =
            s.CitationCount,

        c.Verified =
            s.Verified,

        c.Link =
            LEFT(s.Link, 1024),

        c.LastSeenDataRunId =
            s.DataRunId,

        c.LastSeenAtUtc =
            SYSUTCDATETIME(),

        /* Known association remains review-eligible */
        c.IsCurrentSource =
            1,

        /* Separate operational reporting state */
        c.IsInCurrentReportingWindow =
            s.IsInCurrentReportingWindow,

        c.UpdatedAtUtc =
            SYSUTCDATETIME()

    FROM
        academiconline.FacultyPublicationReviewCandidate c

    JOIN #CurrentReviewSource s
        ON s.FacultyKey =
           c.FacultyKey

       AND s.PublicationKey =
           c.PublicationKey;

    SET @UpdatedRows = @@ROWCOUNT;


    /* --------------------------------------------------------
       Insert new known associations
       -------------------------------------------------------- */

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
        IsInCurrentReportingWindow,

        CreatedAtUtc,
        UpdatedAtUtc
    )
    SELECT
        s.ReviewSourceKey,

        s.FacultyKey,
        s.PublicationKey,

        s.DepartmentId,
        LEFT(s.DepartmentName, 512),
        LEFT(s.DivisionName, 512),

        LEFT(s.UvaUserId, 512),
        LEFT(s.FacultyDisplayName, 512),
        LEFT(s.Email, 512),
        LEFT(s.Position, 512),
        LEFT(s.Orcid, 512),

        LEFT(s.PublicationIdentitySource, 30),
        LEFT(s.SourceAuthorId, 256),

        LEFT(s.RelationshipSource, 60),
        LEFT(s.RelationshipSourceProvider, 60),
        LEFT(s.RelationshipSourceAuthorId, 256),

        LEFT(s.AcademicOnlinePublicationId, 128),
        LEFT(s.OpenAlexId, 128),

        LEFT(s.Doi, 512),
        LEFT(s.DoiNormalized, 512),

        s.Title,
        LEFT(s.Venue, 512),
        LEFT(s.YearText, 50),
        s.PublicationYear,
        s.CitationCount,
        s.Verified,
        LEFT(s.Link, 1024),

        s.DataRunId,
        s.DataRunId,

        SYSUTCDATETIME(),
        SYSUTCDATETIME(),

        1,
        s.IsInCurrentReportingWindow,

        SYSUTCDATETIME(),
        SYSUTCDATETIME()

    FROM #CurrentReviewSource s

    WHERE NOT EXISTS
    (
        SELECT 1

        FROM
            academiconline.FacultyPublicationReviewCandidate c

        WHERE c.FacultyKey =
              s.FacultyKey

          AND c.PublicationKey =
              s.PublicationKey
    );

    SET @InsertedRows = @@ROWCOUNT;


    /* --------------------------------------------------------
       Only retire an association that is absent from the
       entire known review universe.
       -------------------------------------------------------- */

    UPDATE c
    SET
        c.IsCurrentSource = 0,
        c.IsInCurrentReportingWindow = 0,
        c.UpdatedAtUtc = SYSUTCDATETIME()

    FROM
        academiconline.FacultyPublicationReviewCandidate c

    WHERE c.DepartmentId =
          @DepartmentId

      AND c.IsCurrentSource = 1

      AND NOT EXISTS
      (
          SELECT 1

          FROM #CurrentReviewSource s

          WHERE s.FacultyKey =
                c.FacultyKey

            AND s.PublicationKey =
                c.PublicationKey
      );

    SET @RetiredRows = @@ROWCOUNT;


    COMMIT TRANSACTION;


    SELECT
        DepartmentId =
            @DepartmentId,

        SourceRows =
            @SourceRows,

        UpdatedRows =
            @UpdatedRows,

        InsertedRows =
            @InsertedRows,

        RetiredRows =
            @RetiredRows,

        CurrentCandidateRows =
        (
            SELECT COUNT_BIG(*)

            FROM
                academiconline.FacultyPublicationReviewCandidate

            WHERE DepartmentId =
                  @DepartmentId

              AND IsCurrentSource = 1
        );

END;
GO

-- Core auth tables supplied separately; SQL 2019-only OFF index options omitted.
SET ANSI_PADDING ON;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [openalex].[AppRole](
    [AppRoleId] [bigint] IDENTITY(1,1) NOT NULL,
    [RoleName] [varchar](100) NOT NULL,
    [DisplayName] [nvarchar](256) NOT NULL,
    [Description] [nvarchar](1000) NULL,
    [IsEnabled] [bit] NOT NULL,
    [SortOrder] [int] NOT NULL,
    [CreatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_AppRole] PRIMARY KEY CLUSTERED ([AppRoleId] ASC)
 WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_AppRole_RoleName] UNIQUE NONCLUSTERED ([RoleName] ASC)
 WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [openalex].[AppUserRole](
    [AppUserRoleId] [bigint] IDENTITY(1,1) NOT NULL,
    [AppUserId] [bigint] NOT NULL,
    [AppRoleId] [bigint] NOT NULL,
    [IsEnabled] [bit] NOT NULL,
    [GrantedBy] [nvarchar](256) NULL,
    [GrantedAtUtc] [datetime2](7) NOT NULL,
    [RevokedBy] [nvarchar](256) NULL,
    [RevokedAtUtc] [datetime2](7) NULL,
    [CreatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_AppUserRole] PRIMARY KEY CLUSTERED ([AppUserRoleId] ASC)
 WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [UQ_AppUserRole_User_Role] UNIQUE NONCLUSTERED ([AppUserId] ASC, [AppRoleId] ASC)
 WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [openalex].[AuthenticationEvent](
    [AuthenticationEventId] [bigint] IDENTITY(1,1) NOT NULL,
    [UserName] [nvarchar](256) NULL,
    [DisplayName] [nvarchar](256) NULL,
    [Email] [nvarchar](512) NULL,
    [SourceSystem] [varchar](100) NOT NULL,
    [AuthenticationSucceeded] [bit] NOT NULL,
    [FailureReason] [nvarchar](1000) NULL,
    [IpAddress] [nvarchar](100) NULL,
    [UserAgent] [nvarchar](1000) NULL,
    [CreatedAtUtc] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_AuthenticationEvent] PRIMARY KEY CLUSTERED ([AuthenticationEventId] ASC)
 WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [openalex].[AppRole] ADD CONSTRAINT [DF_AppRole_IsEnabled] DEFAULT ((1)) FOR [IsEnabled]
GO
ALTER TABLE [openalex].[AppRole] ADD CONSTRAINT [DF_AppRole_SortOrder] DEFAULT ((100)) FOR [SortOrder]
GO
ALTER TABLE [openalex].[AppRole] ADD CONSTRAINT [DF_AppRole_CreatedAtUtc] DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [openalex].[AppUserRole] ADD CONSTRAINT [DF_AppUserRole_IsEnabled] DEFAULT ((1)) FOR [IsEnabled]
GO
ALTER TABLE [openalex].[AppUserRole] ADD CONSTRAINT [DF_AppUserRole_GrantedAtUtc] DEFAULT (sysutcdatetime()) FOR [GrantedAtUtc]
GO
ALTER TABLE [openalex].[AppUserRole] ADD CONSTRAINT [DF_AppUserRole_CreatedAtUtc] DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [openalex].[AuthenticationEvent] ADD CONSTRAINT [DF_AuthenticationEvent_CreatedAtUtc] DEFAULT (sysutcdatetime()) FOR [CreatedAtUtc]
GO
ALTER TABLE [openalex].[AppUserRole] WITH CHECK ADD CONSTRAINT [FK_AppUserRole_AppRole] FOREIGN KEY([AppRoleId]) REFERENCES [openalex].[AppRole] ([AppRoleId])
GO
ALTER TABLE [openalex].[AppUserRole] CHECK CONSTRAINT [FK_AppUserRole_AppRole]
GO
ALTER TABLE [openalex].[AppUserRole] WITH CHECK ADD CONSTRAINT [FK_AppUserRole_AppUser] FOREIGN KEY([AppUserId]) REFERENCES [openalex].[AppUser] ([AppUserId])
GO
ALTER TABLE [openalex].[AppUserRole] CHECK CONSTRAINT [FK_AppUserRole_AppUser]
GO
COMMIT TRANSACTION;
PRINT N'Installed empty PATCH022 review/auth schema baseline; no application data or user privileges were seeded.';
GO
