/*
V3E PATCH017B1 - Controlled Academic Online-Guided OpenAlex Work Backfill
SQL Server 2016-safe.

Purpose:
  Store the audited execution of ReadyToFetch items from one explicit PATCH017B preview run.

Safety boundary:
  - Inserts canonical OpenAlex work JSON through the existing WorkIngestionRepository path.
  - Does not generate faculty-publication candidates.
  - Does not change faculty-publication decisions.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewRun', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.AcademicOnlineWorkBackfillPreviewRun. Run sql/370_V3E_PATCH017B_AcademicOnlineGuidedWorkBackfillPreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewItem', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.AcademicOnlineWorkBackfillPreviewItem. Run sql/370_V3E_PATCH017B_AcademicOnlineGuidedWorkBackfillPreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlineWorkBackfillRun
    (
        AcademicOnlineWorkBackfillRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourcePreviewRunId BIGINT NOT NULL,
        WorkIngestionRunId BIGINT NULL,
        MaxWorks INT NOT NULL,
        SelectedWorkCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Selected DEFAULT (0),
        InsertedWorkCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Inserted DEFAULT (0),
        AlreadyExistsCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Exists DEFAULT (0),
        InvalidPreviewItemCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Invalid DEFAULT (0),
        FailedItemCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Failed DEFAULT (0),
        AuthorshipRows INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Authorship DEFAULT (0),
        AuthorshipInstitutionRows INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_AuthorshipInstitution DEFAULT (0),
        MatchedInstitutionRows INT NOT NULL CONSTRAINT DF_AOWorkBackfillRun_MatchedInstitution DEFAULT (0),
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
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AOWorkBackfillRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlineWorkBackfillRun PRIMARY KEY CLUSTERED (AcademicOnlineWorkBackfillRunId),
        CONSTRAINT FK_AOWorkBackfillRun_PreviewRun FOREIGN KEY (SourcePreviewRunId)
            REFERENCES openalex.AcademicOnlineWorkBackfillPreviewRun (AcademicOnlineWorkBackfillPreviewRunId)
    );
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlineWorkBackfillItem
    (
        AcademicOnlineWorkBackfillItemId BIGINT IDENTITY(1,1) NOT NULL,
        AcademicOnlineWorkBackfillRunId BIGINT NOT NULL,
        SourcePreviewItemId BIGINT NOT NULL,
        SourcePreviewRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        AcademicOnlinePublicationId NVARCHAR(128) NULL,
        AcademicOnlinePublicationTitle NVARCHAR(MAX) NULL,
        NormalizedOpenAlexId NVARCHAR(128) NULL,
        LocalWorkIdBefore BIGINT NULL,
        WorkIdAfter BIGINT NULL,
        WorkInserted BIT NOT NULL CONSTRAINT DF_AOWorkBackfillItem_Inserted DEFAULT (0),
        ExecutionStatus VARCHAR(100) NOT NULL,
        AuthorshipRows INT NOT NULL CONSTRAINT DF_AOWorkBackfillItem_Authorship DEFAULT (0),
        AuthorshipInstitutionRows INT NOT NULL CONSTRAINT DF_AOWorkBackfillItem_AuthorshipInstitution DEFAULT (0),
        MatchedInstitutionRows INT NOT NULL CONSTRAINT DF_AOWorkBackfillItem_MatchedInstitution DEFAULT (0),
        ErrorText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AOWorkBackfillItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlineWorkBackfillItem PRIMARY KEY CLUSTERED (AcademicOnlineWorkBackfillItemId),
        CONSTRAINT FK_AOWorkBackfillItem_Run FOREIGN KEY (AcademicOnlineWorkBackfillRunId)
            REFERENCES openalex.AcademicOnlineWorkBackfillRun (AcademicOnlineWorkBackfillRunId),
        CONSTRAINT FK_AOWorkBackfillItem_PreviewItem FOREIGN KEY (SourcePreviewItemId)
            REFERENCES openalex.AcademicOnlineWorkBackfillPreviewItem (AcademicOnlineWorkBackfillPreviewItemId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillRun', N'U')
      AND name = N'IX_AOWorkBackfillRun_PreviewRun'
)
BEGIN
    CREATE INDEX IX_AOWorkBackfillRun_PreviewRun
    ON openalex.AcademicOnlineWorkBackfillRun (SourcePreviewRunId, RunStatus, AcademicOnlineWorkBackfillRunId DESC);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillItem', N'U')
      AND name = N'IX_AOWorkBackfillItem_RunStatus'
)
BEGIN
    CREATE INDEX IX_AOWorkBackfillItem_RunStatus
    ON openalex.AcademicOnlineWorkBackfillItem
    (
        AcademicOnlineWorkBackfillRunId,
        ExecutionStatus,
        FacultyId,
        NormalizedOpenAlexId
    );
END;

SELECT
    BackfillRunObjectId = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillRun', N'U'),
    BackfillItemObjectId = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillItem', N'U');
