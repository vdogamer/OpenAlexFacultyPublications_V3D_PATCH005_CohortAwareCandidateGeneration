/*
V3E PATCH016 - Academic Online Source Comparison Snapshot Tables
SQL Server 2016-safe.

Purpose:
  Store read-only snapshots from Academic Online so we can compare:
    - Academic Online scholar profiles
    - Academic Online scholar publications
    - local OpenAlex works/candidates/accepted decisions

This script does not alter existing OpenAlex candidate, match, decision, or score tables.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcademicOnlineComparisonRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlineComparisonRun
    (
        AcademicOnlineComparisonRunId BIGINT IDENTITY(1,1) NOT NULL,
        RunMode VARCHAR(50) NOT NULL,
        PilotCohortName NVARCHAR(200) NULL,
        MaxFacultyToProcess INT NOT NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(100) NOT NULL,
        RunStatus VARCHAR(50) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AcademicOnlineComparisonRun_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AcademicOnlineComparisonRun_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlineComparisonRun PRIMARY KEY CLUSTERED (AcademicOnlineComparisonRunId)
    );
END;

IF OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlineScholarSnapshot
    (
        AcademicOnlineScholarSnapshotId BIGINT IDENTITY(1,1) NOT NULL,
        AcademicOnlineComparisonRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        DepartmentName NVARCHAR(512) NULL,
        AcademicOnlineAuthorId NVARCHAR(128) NULL,
        AcademicOnlineName NVARCHAR(512) NULL,
        AcademicOnlineEmail NVARCHAR(512) NULL,
        AcademicOnlinePosition NVARCHAR(512) NULL,
        AcademicOnlineOrcidUrl NVARCHAR(512) NULL,
        PublicationCount INT NULL,
        HasVerifiedPublications BIT NULL,
        SearchRankingScore DECIMAL(18,6) NULL,
        SearchRawJson NVARCHAR(MAX) NOT NULL,
        GeneralRawJson NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AcademicOnlineScholarSnapshot_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlineScholarSnapshot PRIMARY KEY CLUSTERED (AcademicOnlineScholarSnapshotId)
    );
END;

IF OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcademicOnlinePublicationSnapshot
    (
        AcademicOnlinePublicationSnapshotId BIGINT IDENTITY(1,1) NOT NULL,
        AcademicOnlineComparisonRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        AcademicOnlineAuthorId NVARCHAR(128) NOT NULL,
        IsVerifiedQuery BIT NOT NULL,
        AcademicOnlinePublicationId NVARCHAR(128) NULL,
        Title NVARCHAR(MAX) NULL,
        Venue NVARCHAR(512) NULL,
        YearText NVARCHAR(50) NULL,
        PublicationYear INT NULL,
        CitationCount INT NULL,
        Doi NVARCHAR(512) NULL,
        OpenAlexId NVARCHAR(128) NULL,
        Verified BIT NULL,
        Link NVARCHAR(1024) NULL,
        CoauthorsJson NVARCHAR(MAX) NULL,
        KeywordsJson NVARCHAR(MAX) NULL,
        RawJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AcademicOnlinePublicationSnapshot_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcademicOnlinePublicationSnapshot PRIMARY KEY CLUSTERED (AcademicOnlinePublicationSnapshotId)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U')
      AND name = N'IX_AcademicOnlineScholarSnapshot_RunFaculty'
)
BEGIN
    CREATE INDEX IX_AcademicOnlineScholarSnapshot_RunFaculty
    ON openalex.AcademicOnlineScholarSnapshot (AcademicOnlineComparisonRunId, FacultyId, AcademicOnlineAuthorId);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U')
      AND name = N'IX_AcademicOnlinePublicationSnapshot_RunFaculty'
)
BEGIN
    CREATE INDEX IX_AcademicOnlinePublicationSnapshot_RunFaculty
    ON openalex.AcademicOnlinePublicationSnapshot (AcademicOnlineComparisonRunId, FacultyId, AcademicOnlineAuthorId, IsVerifiedQuery);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U')
      AND name = N'IX_AcademicOnlinePublicationSnapshot_OpenAlexId'
)
BEGIN
    CREATE INDEX IX_AcademicOnlinePublicationSnapshot_OpenAlexId
    ON openalex.AcademicOnlinePublicationSnapshot (OpenAlexId, FacultyId, AcademicOnlineComparisonRunId);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U')
      AND name = N'IX_AcademicOnlinePublicationSnapshot_Doi'
)
BEGIN
    CREATE INDEX IX_AcademicOnlinePublicationSnapshot_Doi
    ON openalex.AcademicOnlinePublicationSnapshot (Doi, FacultyId, AcademicOnlineComparisonRunId);
END;

SELECT
    AcademicOnlineComparisonRunObjectId = OBJECT_ID(N'openalex.AcademicOnlineComparisonRun', N'U'),
    AcademicOnlineScholarSnapshotObjectId = OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U'),
    AcademicOnlinePublicationSnapshotObjectId = OBJECT_ID(N'openalex.AcademicOnlinePublicationSnapshot', N'U');
