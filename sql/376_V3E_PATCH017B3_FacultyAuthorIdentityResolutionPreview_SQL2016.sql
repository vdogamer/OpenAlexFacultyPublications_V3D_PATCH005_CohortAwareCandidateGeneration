/*
V3E PATCH017B3 - Controlled Faculty-Author Identity Resolution Preview
SQL Server 2016-safe.

Purpose:
  Store a preview-only ranking of OpenAlex authors appearing on the works inserted
  by one explicit PATCH017B1 backfill run.

Safety boundary:
  - Does not insert or update FacultyIdentityCandidate.
  - Does not insert or update FacultyIdentityCandidateDecision.
  - Does not insert or update FacultyAuthorMatchDecision.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillRun', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.AcademicOnlineWorkBackfillRun. Run PATCH017B1 setup first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillItem', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.AcademicOnlineWorkBackfillItem. Run PATCH017B1 setup first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyAuthorIdentityResolutionPreviewRun
    (
        FacultyAuthorIdentityResolutionPreviewRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceWorkBackfillRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        MaxWorks INT NOT NULL,
        SelectedWorkCount INT NOT NULL CONSTRAINT DF_FAIRPreviewRun_SelectedWork DEFAULT (0),
        EvaluatedCandidateCount INT NOT NULL CONSTRAINT DF_FAIRPreviewRun_Evaluated DEFAULT (0),
        StrongCandidateCount INT NOT NULL CONSTRAINT DF_FAIRPreviewRun_Strong DEFAULT (0),
        ExactOrcidCandidateCount INT NOT NULL CONSTRAINT DF_FAIRPreviewRun_ExactOrcid DEFAULT (0),
        AmbiguousCandidateCount INT NOT NULL CONSTRAINT DF_FAIRPreviewRun_Ambiguous DEFAULT (0),
        IdentityCandidateRowsBefore INT NULL,
        IdentityCandidateRowsAfter INT NULL,
        IdentityDecisionRowsBefore INT NULL,
        IdentityDecisionRowsAfter INT NULL,
        AuthorDecisionRowsBefore INT NULL,
        AuthorDecisionRowsAfter INT NULL,
        RunStatus VARCHAR(50) NOT NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(100) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FAIRPreviewRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FAIRPreviewRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyAuthorIdentityResolutionPreviewRun PRIMARY KEY CLUSTERED (FacultyAuthorIdentityResolutionPreviewRunId),
        CONSTRAINT FK_FAIRPreviewRun_BackfillRun FOREIGN KEY (SourceWorkBackfillRunId)
            REFERENCES openalex.AcademicOnlineWorkBackfillRun (AcademicOnlineWorkBackfillRunId)
    );
END;

IF OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewCandidate', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyAuthorIdentityResolutionPreviewCandidate
    (
        FacultyAuthorIdentityResolutionPreviewCandidateId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyAuthorIdentityResolutionPreviewRunId BIGINT NOT NULL,
        SourceWorkBackfillRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        FacultyOrcid NVARCHAR(256) NULL,
        FacultyClaimedOpenAlexAuthorId NVARCHAR(128) NULL,
        AcademicOnlineName NVARCHAR(512) NULL,
        AcademicOnlineEmail NVARCHAR(512) NULL,
        AcademicOnlineOrcidUrl NVARCHAR(512) NULL,
        SourceWorkCount INT NOT NULL,
        CandidateAuthorOpenAlexId NVARCHAR(128) NOT NULL,
        CandidateAuthorDisplayName NVARCHAR(512) NULL,
        CandidateAuthorOrcid NVARCHAR(256) NULL,
        WorkAppearanceCount INT NOT NULL,
        CorrespondingAuthorWorkCount INT NOT NULL,
        AuthorshipInstitutionCount INT NOT NULL,
        UvaAffiliationWorkCount INT NOT NULL,
        AppearingWorkOpenAlexIds NVARCHAR(MAX) NULL,
        ExistingIdentitySummaryId BIGINT NULL,
        ExistingIdentityTriageStatus NVARCHAR(100) NULL,
        ExistingIdentityEvidenceScore DECIMAL(18,4) NULL,
        CurrentIdentityDecisionStatus NVARCHAR(100) NULL,
        ExistingIdentityCandidateDiscoveryCount INT NOT NULL,
        CurrentAcceptedAuthorDecisionCount INT NOT NULL,
        ExactFacultyOrcidMatch BIT NOT NULL,
        ExactAcademicOnlineOrcidMatch BIT NOT NULL,
        ExactClaimedOpenAlexIdMatch BIT NOT NULL,
        NameEvidenceScore INT NOT NULL,
        AppearsOnAllSelectedWorks BIT NOT NULL,
        ProposedIdentityScore DECIMAL(18,4) NOT NULL,
        CandidateRank INT NOT NULL,
        PreviewStatus VARCHAR(100) NOT NULL,
        ProposedNextAction VARCHAR(150) NOT NULL,
        EvidenceSummary NVARCHAR(MAX) NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FAIRPreviewCandidate_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyAuthorIdentityResolutionPreviewCandidate PRIMARY KEY CLUSTERED (FacultyAuthorIdentityResolutionPreviewCandidateId),
        CONSTRAINT FK_FAIRPreviewCandidate_Run FOREIGN KEY (FacultyAuthorIdentityResolutionPreviewRunId)
            REFERENCES openalex.FacultyAuthorIdentityResolutionPreviewRun (FacultyAuthorIdentityResolutionPreviewRunId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewRun', N'U')
      AND name = N'IX_FAIRPreviewRun_SourceFaculty'
)
BEGIN
    CREATE INDEX IX_FAIRPreviewRun_SourceFaculty
    ON openalex.FacultyAuthorIdentityResolutionPreviewRun
    (
        SourceWorkBackfillRunId,
        FacultyId,
        RunStatus,
        FacultyAuthorIdentityResolutionPreviewRunId DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewCandidate', N'U')
      AND name = N'IX_FAIRPreviewCandidate_RunRank'
)
BEGIN
    CREATE INDEX IX_FAIRPreviewCandidate_RunRank
    ON openalex.FacultyAuthorIdentityResolutionPreviewCandidate
    (
        FacultyAuthorIdentityResolutionPreviewRunId,
        CandidateRank,
        PreviewStatus,
        ProposedIdentityScore DESC
    );
END;

SELECT
    PreviewRunObjectId = OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewRun', N'U'),
    PreviewCandidateObjectId = OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewCandidate', N'U');
