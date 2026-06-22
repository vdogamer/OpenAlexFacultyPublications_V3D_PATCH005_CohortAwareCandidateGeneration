/*
V3E PATCH002 - Work Expansion by Candidate Author ID
SQL Server 2016-safe.

Purpose:
  Store OpenAlex works retrieved by candidate author ID before using them for publication matching.
  These rows are evidence for identity disambiguation, not accepted faculty publications.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyIdentityCandidateWork
    (
        FacultyIdentityCandidateWorkId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        WorkOpenAlexId VARCHAR(128) NOT NULL,
        WorkOpenAlexShortId VARCHAR(64) NULL,
        WorkDisplayName NVARCHAR(MAX) NULL,
        WorkDoi NVARCHAR(512) NULL,
        PublicationYear INT NULL,
        PublicationDate DATE NULL,
        WorkType NVARCHAR(100) NULL,
        MatchedAuthorDisplayName NVARCHAR(512) NULL,
        MatchedAuthorPosition NVARCHAR(50) NULL,
        MatchedAuthorInstitutionsJson NVARCHAR(MAX) NULL,
        AuthorshipsJson NVARCHAR(MAX) NULL,
        PrimaryLocationJson NVARCHAR(MAX) NULL,
        LocationsJson NVARCHAR(MAX) NULL,
        WorkRawJson NVARCHAR(MAX) NULL,
        EvidenceSummary NVARCHAR(MAX) NULL,
        IngestionRunId BIGINT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidateWork_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidateWork_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyIdentityCandidateWork PRIMARY KEY CLUSTERED (FacultyIdentityCandidateWorkId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U')
      AND name = N'UX_FacultyIdentityCandidateWork_Faculty_Author_Work'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyIdentityCandidateWork_Faculty_Author_Work
    ON openalex.FacultyIdentityCandidateWork
    (
        FacultyId,
        CandidateAuthorOpenAlexId,
        WorkOpenAlexId
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U')
      AND name = N'IX_FacultyIdentityCandidateWork_Faculty_Author'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityCandidateWork_Faculty_Author
    ON openalex.FacultyIdentityCandidateWork
    (
        FacultyId,
        CandidateAuthorOpenAlexShortId,
        PublicationYear DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U')
      AND name = N'IX_FacultyIdentityCandidateWork_WorkOpenAlexShortId'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityCandidateWork_WorkOpenAlexShortId
    ON openalex.FacultyIdentityCandidateWork
    (
        WorkOpenAlexShortId
    );
END;

SELECT
    OBJECT_SCHEMA_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U')) AS SchemaName,
    OBJECT_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidateWork', N'U')) AS TableName,
    COUNT(*) AS ExistingRows
FROM openalex.FacultyIdentityCandidateWork;
