/*
V3E PATCH017B - Academic Online-Guided OpenAlex Work Backfill Preview
SQL Server 2016-safe.

This schema stores preview/audit results only.
It does not insert into openalex.Work, create publication candidates, or alter decisions.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlineWorkBackfillPreviewRun
    (
        AcademicOnlineWorkBackfillPreviewRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceAcademicOnlineComparisonRunId BIGINT NOT NULL,
        FacultyId BIGINT NULL,
        MaxWorks INT NOT NULL,
        SelectedWorkCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Selected DEFAULT (0),
        ReadyToFetchCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Ready DEFAULT (0),
        AlreadyExistsLocallyCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Exists DEFAULT (0),
        MissingOpenAlexIdCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_MissingId DEFAULT (0),
        NotFoundByOpenAlexCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_NotFound DEFAULT (0),
        FetchFailedCount INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Failed DEFAULT (0),
        DuplicateSnapshotRecordsSkipped INT NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Duplicates DEFAULT (0),
        RunStatus VARCHAR(50) NOT NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(100) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlineWorkBackfillPreviewRun PRIMARY KEY CLUSTERED (AcademicOnlineWorkBackfillPreviewRunId)
    );
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlineWorkBackfillPreviewItem
    (
        AcademicOnlineWorkBackfillPreviewItemId BIGINT IDENTITY(1,1) NOT NULL,
        AcademicOnlineWorkBackfillPreviewRunId BIGINT NOT NULL,
        SourceAcademicOnlineComparisonRunId BIGINT NOT NULL,
        AcademicOnlinePublicationSnapshotId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        AcademicOnlineScholarName NVARCHAR(512) NULL,
        AcademicOnlinePublicationId NVARCHAR(128) NULL,
        AcademicOnlinePublicationTitle NVARCHAR(MAX) NULL,
        AcademicOnlinePublicationYear INT NULL,
        AcademicOnlineDoi NVARCHAR(512) NULL,
        AcademicOnlineOpenAlexId NVARCHAR(128) NULL,
        NormalizedOpenAlexId NVARCHAR(128) NULL,
        SnapshotDuplicateCount INT NOT NULL,
        LocalWorkIdAtSelection BIGINT NULL,
        PreviewStatus VARCHAR(100) NOT NULL,
        OpenAlexRequestUriMasked NVARCHAR(2048) NULL,
        OpenAlexHttpStatusCode INT NULL,
        OpenAlexResponseContentLength BIGINT NULL,
        ReturnedOpenAlexId NVARCHAR(128) NULL,
        ReturnedTitle NVARCHAR(MAX) NULL,
        ReturnedDoi NVARCHAR(512) NULL,
        ReturnedPublicationYear INT NULL,
        ReturnedWorkType NVARCHAR(100) NULL,
        ReturnedCitedByCount INT NULL,
        RawResponseJson NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AOWorkBackfillPreviewItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlineWorkBackfillPreviewItem PRIMARY KEY CLUSTERED (AcademicOnlineWorkBackfillPreviewItemId),
        CONSTRAINT FK_AOWorkBackfillPreviewItem_Run FOREIGN KEY (AcademicOnlineWorkBackfillPreviewRunId)
            REFERENCES openalex.AcademicOnlineWorkBackfillPreviewRun (AcademicOnlineWorkBackfillPreviewRunId)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewItem', N'U')
      AND name = N'IX_AOWorkBackfillPreviewItem_RunStatus'
)
BEGIN
    CREATE INDEX IX_AOWorkBackfillPreviewItem_RunStatus
    ON openalex.AcademicOnlineWorkBackfillPreviewItem
    (
        AcademicOnlineWorkBackfillPreviewRunId,
        PreviewStatus,
        FacultyId,
        NormalizedOpenAlexId
    );
END;

SELECT
    PreviewRunObjectId = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewRun', N'U'),
    PreviewItemObjectId = OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewItem', N'U');
