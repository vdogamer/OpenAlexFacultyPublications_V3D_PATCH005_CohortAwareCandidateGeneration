/*
V3E PATCH003 - Identity Work Evidence Scoring
SQL Server 2016-safe.

Purpose:
  Score expanded works from openalex.FacultyIdentityCandidateWork as evidence for or against a candidate author identity.
  These scores are review evidence only. They are not accepted identities or accepted publications.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyIdentityWorkEvidenceScore
    (
        FacultyIdentityWorkEvidenceScoreId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        WorkOpenAlexId VARCHAR(128) NOT NULL,
        WorkOpenAlexShortId VARCHAR(64) NULL,
        MatchedAuthorDisplayName NVARCHAR(512) NULL,
        WorkDisplayName NVARCHAR(MAX) NULL,
        PublicationYear INT NULL,
        NameEvidenceScore DECIMAL(9,4) NOT NULL,
        InstitutionEvidenceScore DECIMAL(9,4) NOT NULL,
        DoiEvidenceScore DECIMAL(9,4) NOT NULL,
        PublicationYearEvidenceScore DECIMAL(9,4) NOT NULL,
        OverallWorkEvidenceScore DECIMAL(9,4) NOT NULL,
        WorkEvidenceStatus VARCHAR(50) NOT NULL,
        EvidenceSummary NVARCHAR(MAX) NULL,
        EvidenceJson NVARCHAR(MAX) NULL,
        IngestionRunId BIGINT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityWorkEvidenceScore_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityWorkEvidenceScore_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyIdentityWorkEvidenceScore PRIMARY KEY CLUSTERED (FacultyIdentityWorkEvidenceScoreId),
        CONSTRAINT CK_FacultyIdentityWorkEvidenceScore_Overall CHECK (OverallWorkEvidenceScore >= 0 AND OverallWorkEvidenceScore <= 100),
        CONSTRAINT CK_FacultyIdentityWorkEvidenceScore_Status CHECK (WorkEvidenceStatus IN ('StrongWorkEvidence', 'ModerateWorkEvidence', 'WeakWorkEvidence', 'ConflictingWorkEvidence', 'NeedsManualReview'))
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U')
      AND name = N'UX_FacultyIdentityWorkEvidenceScore_Faculty_Author_Work'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyIdentityWorkEvidenceScore_Faculty_Author_Work
    ON openalex.FacultyIdentityWorkEvidenceScore
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
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U')
      AND name = N'IX_FacultyIdentityWorkEvidenceScore_Faculty_Author_Score'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityWorkEvidenceScore_Faculty_Author_Score
    ON openalex.FacultyIdentityWorkEvidenceScore
    (
        FacultyId,
        CandidateAuthorOpenAlexShortId,
        OverallWorkEvidenceScore DESC,
        PublicationYear DESC
    );
END;

SELECT
    OBJECT_SCHEMA_NAME(OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U')) AS SchemaName,
    OBJECT_NAME(OBJECT_ID(N'openalex.FacultyIdentityWorkEvidenceScore', N'U')) AS TableName,
    COUNT(*) AS ExistingRows
FROM openalex.FacultyIdentityWorkEvidenceScore;
