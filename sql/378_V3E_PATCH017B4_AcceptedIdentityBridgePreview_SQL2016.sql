/*
V3E PATCH017B4 - Controlled Accepted-Identity Bridge Preview
SQL Server 2016-safe.

Purpose:
  Store a preview of the existing AcceptedIdentityPublicationBridge behavior for
  accepted identity decisions selected through one explicit PATCH017B3 run.

Safety boundary:
  - Does not write openalex.FacultyAuthorMatchCandidate.
  - Does not write openalex.FacultyAuthorMatchDecision.
  - Does not write publication candidates or decisions.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewRun', N'U') IS NULL
BEGIN
    RAISERROR('Missing PATCH017B3 preview tables. Run sql/376_V3E_PATCH017B3_FacultyAuthorIdentityResolutionPreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityBridgePreviewRun
    (
        AcceptedIdentityBridgePreviewRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceIdentityResolutionPreviewRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        MaxIdentities INT NOT NULL,
        SelectedAcceptedIdentityCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_Selected DEFAULT (0),
        ReadyCandidateInsertCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_CandidateInsert DEFAULT (0),
        ReadyCandidateUpdateCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_CandidateUpdate DEFAULT (0),
        ReadyDecisionInsertCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_DecisionInsert DEFAULT (0),
        AlreadyAcceptedDecisionCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_AlreadyAccepted DEFAULT (0),
        MissingIdentitySummaryCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_MissingSummary DEFAULT (0),
        MissingLocalAuthorCount INT NOT NULL CONSTRAINT DF_AIBridgePreviewRun_MissingAuthor DEFAULT (0),
        AuthorCandidateRowsBefore INT NULL,
        AuthorCandidateRowsAfter INT NULL,
        AuthorDecisionRowsBefore INT NULL,
        AuthorDecisionRowsAfter INT NULL,
        PublicationCandidateRowsBefore INT NULL,
        PublicationCandidateRowsAfter INT NULL,
        PublicationDecisionRowsBefore INT NULL,
        PublicationDecisionRowsAfter INT NULL,
        RunStatus VARCHAR(50) NOT NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(100) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgePreviewRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgePreviewRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcceptedIdentityBridgePreviewRun PRIMARY KEY CLUSTERED (AcceptedIdentityBridgePreviewRunId),
        CONSTRAINT FK_AIBridgePreviewRun_IdentityPreviewRun FOREIGN KEY (SourceIdentityResolutionPreviewRunId)
            REFERENCES openalex.FacultyAuthorIdentityResolutionPreviewRun (FacultyAuthorIdentityResolutionPreviewRunId),
        CONSTRAINT CK_AIBridgePreviewRun_MaxIdentities CHECK (MaxIdentities BETWEEN 1 AND 5)
    );
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityBridgePreviewItem
    (
        AcceptedIdentityBridgePreviewItemId BIGINT IDENTITY(1,1) NOT NULL,
        AcceptedIdentityBridgePreviewRunId BIGINT NOT NULL,
        SourceIdentityResolutionPreviewRunId BIGINT NOT NULL,
        FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        FacultyIdentityCandidateSummaryId BIGINT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        CandidateDisplayName NVARCHAR(512) NULL,
        CandidateOrcid NVARCHAR(256) NULL,
        BestIdentityEvidenceScore DECIMAL(18,4) NULL,
        LocalAuthorId BIGINT NULL,
        ExistingBridgeCandidateId BIGINT NULL,
        ExistingBridgeCandidateStatus VARCHAR(100) NULL,
        ExistingBridgeCandidateMatchScore DECIMAL(18,4) NULL,
        ExistingAcceptedAuthorDecisionId BIGINT NULL,
        CurrentAuthorDecisionCount INT NOT NULL,
        ProposedCandidateAction VARCHAR(100) NOT NULL,
        ProposedDecisionAction VARCHAR(100) NOT NULL,
        PreviewStatus VARCHAR(100) NOT NULL,
        ProposedNextAction VARCHAR(150) NOT NULL,
        EvidenceSummary NVARCHAR(MAX) NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgePreviewItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcceptedIdentityBridgePreviewItem PRIMARY KEY CLUSTERED (AcceptedIdentityBridgePreviewItemId),
        CONSTRAINT FK_AIBridgePreviewItem_Run FOREIGN KEY (AcceptedIdentityBridgePreviewRunId)
            REFERENCES openalex.AcceptedIdentityBridgePreviewRun (AcceptedIdentityBridgePreviewRunId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U')
      AND name = N'IX_AIBridgePreviewRun_SourceFaculty'
)
BEGIN
    CREATE INDEX IX_AIBridgePreviewRun_SourceFaculty
    ON openalex.AcceptedIdentityBridgePreviewRun
    (
        SourceIdentityResolutionPreviewRunId,
        FacultyId,
        RunStatus,
        AcceptedIdentityBridgePreviewRunId DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewItem', N'U')
      AND name = N'IX_AIBridgePreviewItem_RunStatus'
)
BEGIN
    CREATE INDEX IX_AIBridgePreviewItem_RunStatus
    ON openalex.AcceptedIdentityBridgePreviewItem
    (
        AcceptedIdentityBridgePreviewRunId,
        PreviewStatus,
        CandidateAuthorOpenAlexId
    );
END;

SELECT
    PreviewRunObjectId = OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U'),
    PreviewItemObjectId = OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewItem', N'U');
