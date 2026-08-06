/*
V3E PATCH017B5 - Controlled Accepted-Identity Bridge Execution
SQL Server 2016-safe.

Purpose:
  Audit the controlled execution that converts selected PATCH017B4 preview rows
  into accepted faculty-author match candidates and decisions.

Safety boundary:
  - Requires one explicit completed PATCH017B4 preview run.
  - Does not create faculty-publication candidates.
  - Does not alter faculty-publication decisions.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U') IS NULL
BEGIN
    RAISERROR('Missing PATCH017B4 preview tables. Run sql/378_V3E_PATCH017B4_AcceptedIdentityBridgePreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityBridgeExecutionRun
    (
        AcceptedIdentityBridgeExecutionRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceAcceptedIdentityBridgePreviewRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        MaxIdentities INT NOT NULL,
        ConfirmedBridge BIT NOT NULL,
        IngestionRunId BIGINT NULL,
        SelectedIdentityCount INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_Selected DEFAULT (0),
        CandidateRowsInserted INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_CandidateInserted DEFAULT (0),
        CandidateRowsUpdated INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_CandidateUpdated DEFAULT (0),
        DecisionRowsInserted INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_DecisionInserted DEFAULT (0),
        DecisionsAlreadyAccepted INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_AlreadyAccepted DEFAULT (0),
        MissingLocalAuthorCount INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_MissingAuthor DEFAULT (0),
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
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcceptedIdentityBridgeExecutionRun PRIMARY KEY CLUSTERED (AcceptedIdentityBridgeExecutionRunId),
        CONSTRAINT FK_AIBridgeExecutionRun_PreviewRun FOREIGN KEY (SourceAcceptedIdentityBridgePreviewRunId)
            REFERENCES openalex.AcceptedIdentityBridgePreviewRun (AcceptedIdentityBridgePreviewRunId),
        CONSTRAINT CK_AIBridgeExecutionRun_MaxIdentities CHECK (MaxIdentities BETWEEN 1 AND 5),
        CONSTRAINT CK_AIBridgeExecutionRun_Confirmed CHECK (ConfirmedBridge = 1)
    );
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityBridgeExecutionItem
    (
        AcceptedIdentityBridgeExecutionItemId BIGINT IDENTITY(1,1) NOT NULL,
        AcceptedIdentityBridgeExecutionRunId BIGINT NOT NULL,
        SourceAcceptedIdentityBridgePreviewItemId BIGINT NOT NULL,
        FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
        FacultyIdentityCandidateSummaryId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        CandidateDisplayName NVARCHAR(512) NULL,
        CandidateOrcid NVARCHAR(256) NULL,
        LocalAuthorId BIGINT NULL,
        BridgeCandidateIdBefore BIGINT NULL,
        BridgeCandidateIdAfter BIGINT NOT NULL,
        AcceptedAuthorDecisionIdBefore BIGINT NULL,
        AcceptedAuthorDecisionIdAfter BIGINT NOT NULL,
        CandidateExecutionAction VARCHAR(100) NOT NULL,
        DecisionExecutionAction VARCHAR(100) NOT NULL,
        ExecutionStatus VARCHAR(50) NOT NULL,
        WarningText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgeExecutionItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcceptedIdentityBridgeExecutionItem PRIMARY KEY CLUSTERED (AcceptedIdentityBridgeExecutionItemId),
        CONSTRAINT FK_AIBridgeExecutionItem_Run FOREIGN KEY (AcceptedIdentityBridgeExecutionRunId)
            REFERENCES openalex.AcceptedIdentityBridgeExecutionRun (AcceptedIdentityBridgeExecutionRunId),
        CONSTRAINT FK_AIBridgeExecutionItem_PreviewItem FOREIGN KEY (SourceAcceptedIdentityBridgePreviewItemId)
            REFERENCES openalex.AcceptedIdentityBridgePreviewItem (AcceptedIdentityBridgePreviewItemId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U')
      AND name = N'IX_AIBridgeExecutionRun_SourceFaculty'
)
BEGIN
    CREATE INDEX IX_AIBridgeExecutionRun_SourceFaculty
    ON openalex.AcceptedIdentityBridgeExecutionRun
    (
        SourceAcceptedIdentityBridgePreviewRunId,
        FacultyId,
        RunStatus,
        AcceptedIdentityBridgeExecutionRunId DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U')
      AND name = N'IX_AIBridgeExecutionItem_RunAuthor'
)
BEGIN
    CREATE INDEX IX_AIBridgeExecutionItem_RunAuthor
    ON openalex.AcceptedIdentityBridgeExecutionItem
    (
        AcceptedIdentityBridgeExecutionRunId,
        CandidateAuthorOpenAlexId,
        ExecutionStatus
    );
END;

SELECT
    ExecutionRunObjectId = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U'),
    ExecutionItemObjectId = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U');
