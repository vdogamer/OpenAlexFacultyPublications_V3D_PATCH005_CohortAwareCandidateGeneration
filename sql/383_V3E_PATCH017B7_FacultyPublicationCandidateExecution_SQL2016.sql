/*
V3E PATCH017B7 - Controlled Faculty-Publication Candidate Execution
SQL Server 2016-safe.

Purpose:
  Persist the audit trail for a tightly controlled execution that converts frozen
  PATCH017B6 ReadyToGenerate preview rows into real FacultyPublicationCandidate
  rows with CandidateStatus = PendingReview.

Safety boundary:
  - Requires one explicit completed PATCH017B6 preview run.
  - Inserts/reuses only CandidateSource = AcceptedAuthorMatch rows.
  - Does not insert, update, supersede, or otherwise alter FacultyPublicationDecision.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U') IS NULL
   OR OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewItem', N'U') IS NULL
BEGIN
    RAISERROR('Missing PATCH017B2/B6 preview tables. Run sql/374_V3E_PATCH017B2_FacultyPublicationCandidatePreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.FacultyPublicationCandidate. Run the faculty-publication candidate schema first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.FacultyPublicationDecision. Run the publication review schema first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCandidateExecutionRun
    (
        FacultyPublicationCandidateExecutionRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceFacultyPublicationCandidatePreviewRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        MaxCandidates INT NOT NULL,
        ConfirmedExecution BIT NOT NULL,
        IngestionRunId BIGINT NULL,
        SelectedCandidateCount INT NOT NULL CONSTRAINT DF_FPCExecutionRun_Selected DEFAULT (0),
        CandidateRowsInserted INT NOT NULL CONSTRAINT DF_FPCExecutionRun_Inserted DEFAULT (0),
        CandidatesAlreadyExist INT NOT NULL CONSTRAINT DF_FPCExecutionRun_Existing DEFAULT (0),
        ValidationFailureCount INT NOT NULL CONSTRAINT DF_FPCExecutionRun_ValidationFailure DEFAULT (0),
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
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FPCExecutionRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FPCExecutionRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyPublicationCandidateExecutionRun
            PRIMARY KEY CLUSTERED (FacultyPublicationCandidateExecutionRunId),
        CONSTRAINT FK_FPCExecutionRun_PreviewRun
            FOREIGN KEY (SourceFacultyPublicationCandidatePreviewRunId)
            REFERENCES openalex.FacultyPublicationCandidatePreviewRun (FacultyPublicationCandidatePreviewRunId),
        CONSTRAINT CK_FPCExecutionRun_MaxCandidates CHECK (MaxCandidates BETWEEN 1 AND 5),
        CONSTRAINT CK_FPCExecutionRun_Confirmed CHECK (ConfirmedExecution = 1),
        CONSTRAINT CK_FPCExecutionRun_Status CHECK (RunStatus IN ('Running', 'Completed', 'Failed'))
    );
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCandidateExecutionItem
    (
        FacultyPublicationCandidateExecutionItemId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyPublicationCandidateExecutionRunId BIGINT NOT NULL,
        SourceFacultyPublicationCandidatePreviewItemId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        WorkId BIGINT NOT NULL,
        WorkOpenAlexId VARCHAR(64) NOT NULL,
        WorkTitle NVARCHAR(MAX) NULL,
        FacultyAuthorMatchDecisionId BIGINT NOT NULL,
        SelectedAuthorOpenAlexId VARCHAR(64) NOT NULL,
        WorkAuthorshipId BIGINT NOT NULL,
        ProposedMatchScore DECIMAL(9,4) NOT NULL,
        CandidateIdBefore BIGINT NULL,
        CandidateIdAfter BIGINT NOT NULL,
        CandidateExecutionAction VARCHAR(100) NOT NULL,
        ExecutionStatus VARCHAR(50) NOT NULL,
        WarningText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FPCExecutionItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyPublicationCandidateExecutionItem
            PRIMARY KEY CLUSTERED (FacultyPublicationCandidateExecutionItemId),
        CONSTRAINT FK_FPCExecutionItem_Run
            FOREIGN KEY (FacultyPublicationCandidateExecutionRunId)
            REFERENCES openalex.FacultyPublicationCandidateExecutionRun (FacultyPublicationCandidateExecutionRunId),
        CONSTRAINT FK_FPCExecutionItem_PreviewItem
            FOREIGN KEY (SourceFacultyPublicationCandidatePreviewItemId)
            REFERENCES openalex.FacultyPublicationCandidatePreviewItem (FacultyPublicationCandidatePreviewItemId),
        CONSTRAINT FK_FPCExecutionItem_CandidateAfter
            FOREIGN KEY (CandidateIdAfter)
            REFERENCES openalex.FacultyPublicationCandidate (FacultyPublicationCandidateId),
        CONSTRAINT CK_FPCExecutionItem_Status CHECK (ExecutionStatus = 'Completed')
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionRun', N'U')
      AND name = N'IX_FPCExecutionRun_SourceFaculty'
)
BEGIN
    CREATE INDEX IX_FPCExecutionRun_SourceFaculty
    ON openalex.FacultyPublicationCandidateExecutionRun
    (
        SourceFacultyPublicationCandidatePreviewRunId,
        FacultyId,
        RunStatus,
        FacultyPublicationCandidateExecutionRunId DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionItem', N'U')
      AND name = N'IX_FPCExecutionItem_RunWork'
)
BEGIN
    CREATE INDEX IX_FPCExecutionItem_RunWork
    ON openalex.FacultyPublicationCandidateExecutionItem
    (
        FacultyPublicationCandidateExecutionRunId,
        WorkId,
        ExecutionStatus
    );
END;

SELECT
    ExecutionRunObjectId = OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionRun', N'U'),
    ExecutionItemObjectId = OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionItem', N'U');
