/*
V3E PATCH017B2 - Controlled Faculty-Publication Candidate Generation Preview
SQL Server 2016-safe.

Purpose:
  Store a read-only preview of proposed FacultyPublicationCandidate rows for works inserted
  by one explicit completed PATCH017B1 work-backfill run.

Safety boundary:
  - Does not insert or update openalex.FacultyPublicationCandidate.
  - Does not insert or update openalex.FacultyPublicationDecision.
  - Limits the worker to five works.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillRun', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.AcademicOnlineWorkBackfillRun. Run PATCH017B1 setup and complete a controlled work backfill first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillItem', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.AcademicOnlineWorkBackfillItem. Run PATCH017B1 setup and complete a controlled work backfill first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCandidatePreviewRun
    (
        FacultyPublicationCandidatePreviewRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceWorkBackfillRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        MaxWorks INT NOT NULL,
        SelectedWorkCount INT NOT NULL CONSTRAINT DF_FPCPreviewRun_Selected DEFAULT (0),
        ReadyToGenerateCount INT NOT NULL CONSTRAINT DF_FPCPreviewRun_Ready DEFAULT (0),
        ExistingCandidateCount INT NOT NULL CONSTRAINT DF_FPCPreviewRun_Existing DEFAULT (0),
        NoAcceptedAuthorDecisionCount INT NOT NULL CONSTRAINT DF_FPCPreviewRun_NoDecision DEFAULT (0),
        MultipleAcceptedAuthorDecisionCount INT NOT NULL CONSTRAINT DF_FPCPreviewRun_MultipleDecision DEFAULT (0),
        AcceptedAuthorNotOnWorkCount INT NOT NULL CONSTRAINT DF_FPCPreviewRun_AuthorNotOnWork DEFAULT (0),
        CandidateRowsBefore INT NULL,
        CandidateRowsAfter INT NULL,
        DecisionRowsBefore INT NULL,
        DecisionRowsAfter INT NULL,
        RunStatus VARCHAR(50) NOT NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(100) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FPCPreviewRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FPCPreviewRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyPublicationCandidatePreviewRun PRIMARY KEY CLUSTERED (FacultyPublicationCandidatePreviewRunId),
        CONSTRAINT FK_FPCPreviewRun_BackfillRun FOREIGN KEY (SourceWorkBackfillRunId)
            REFERENCES openalex.AcademicOnlineWorkBackfillRun (AcademicOnlineWorkBackfillRunId)
    );
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCandidatePreviewItem
    (
        FacultyPublicationCandidatePreviewItemId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyPublicationCandidatePreviewRunId BIGINT NOT NULL,
        SourceBackfillItemId BIGINT NOT NULL,
        SourceWorkBackfillRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        WorkId BIGINT NOT NULL,
        WorkOpenAlexId NVARCHAR(128) NOT NULL,
        WorkOpenAlexShortId VARCHAR(32) NULL,
        WorkTitle NVARCHAR(MAX) NULL,
        WorkDoi NVARCHAR(512) NULL,
        PublicationYear INT NULL,
        PublicationDate DATE NULL,
        WorkType VARCHAR(100) NULL,
        AcceptedDecisionCount INT NOT NULL,
        FacultyAuthorMatchDecisionId BIGINT NULL,
        AuthorId BIGINT NULL,
        AuthorOpenAlexId VARCHAR(64) NULL,
        AuthorDisplayName NVARCHAR(512) NULL,
        AuthorOrcid NVARCHAR(256) NULL,
        WorkAuthorshipId BIGINT NULL,
        OrdinalPosition INT NULL,
        IsCorresponding BIT NULL,
        AuthorshipInstitutionCount INT NOT NULL,
        MatchingInstitutionCount INT NOT NULL,
        ExistingCandidateId BIGINT NULL,
        ProposedCandidateSource VARCHAR(100) NOT NULL,
        ProposedCandidateStatus VARCHAR(100) NOT NULL,
        ProposedMatchScore DECIMAL(9,4) NOT NULL,
        PreviewStatus VARCHAR(100) NOT NULL,
        EvidenceSummary NVARCHAR(MAX) NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FPCPreviewItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyPublicationCandidatePreviewItem PRIMARY KEY CLUSTERED (FacultyPublicationCandidatePreviewItemId),
        CONSTRAINT FK_FPCPreviewItem_PreviewRun FOREIGN KEY (FacultyPublicationCandidatePreviewRunId)
            REFERENCES openalex.FacultyPublicationCandidatePreviewRun (FacultyPublicationCandidatePreviewRunId),
        CONSTRAINT FK_FPCPreviewItem_BackfillItem FOREIGN KEY (SourceBackfillItemId)
            REFERENCES openalex.AcademicOnlineWorkBackfillItem (AcademicOnlineWorkBackfillItemId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U')
      AND name = N'IX_FPCPreviewRun_SourceFaculty'
)
BEGIN
    CREATE INDEX IX_FPCPreviewRun_SourceFaculty
    ON openalex.FacultyPublicationCandidatePreviewRun
    (
        SourceWorkBackfillRunId,
        FacultyId,
        RunStatus,
        FacultyPublicationCandidatePreviewRunId DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewItem', N'U')
      AND name = N'IX_FPCPreviewItem_RunStatus'
)
BEGIN
    CREATE INDEX IX_FPCPreviewItem_RunStatus
    ON openalex.FacultyPublicationCandidatePreviewItem
    (
        FacultyPublicationCandidatePreviewRunId,
        PreviewStatus,
        FacultyId,
        WorkId
    );
END;

SELECT
    PreviewRunObjectId = OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U'),
    PreviewItemObjectId = OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewItem', N'U');
