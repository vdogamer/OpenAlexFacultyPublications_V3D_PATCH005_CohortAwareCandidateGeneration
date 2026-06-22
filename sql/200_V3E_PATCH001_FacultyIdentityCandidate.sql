/*
V3E PATCH001 - Faculty Identity Candidate Discovery
SQL Server 2016-safe.

Purpose:
  Store possible OpenAlex author identities for a faculty member before any human acceptance/rejection.
  This table is intentionally separate from openalex.FacultyAuthorMatchCandidate and openalex.FacultyAuthorMatchDecision.

Important:
  These rows are candidates for review. They are not accepted author identities.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyIdentityCandidate
    (
        FacultyIdentityCandidateId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        CandidateDisplayName NVARCHAR(512) NULL,
        CandidateOrcid NVARCHAR(256) NULL,
        WorksCount INT NULL,
        CitedByCount INT NULL,
        LastKnownInstitutionsJson NVARCHAR(MAX) NULL,
        DiscoveryMethod VARCHAR(100) NOT NULL,
        EvidenceScore DECIMAL(9,4) NOT NULL,
        EvidenceStatus VARCHAR(50) NOT NULL,
        EvidenceSummary NVARCHAR(MAX) NULL,
        EvidenceJson NVARCHAR(MAX) NULL,
        IngestionRunId BIGINT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidate_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidate_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyIdentityCandidate PRIMARY KEY CLUSTERED (FacultyIdentityCandidateId),
        CONSTRAINT CK_FacultyIdentityCandidate_EvidenceScore CHECK (EvidenceScore >= 0 AND EvidenceScore <= 100),
        CONSTRAINT CK_FacultyIdentityCandidate_EvidenceStatus CHECK (EvidenceStatus IN ('CandidateForReview', 'StrongIdentifierCandidate', 'PossibleNameCandidate', 'WeakNameCandidate', 'NeedsMoreEvidence'))
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U')
      AND name = N'UX_FacultyIdentityCandidate_Faculty_Author_Method'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyIdentityCandidate_Faculty_Author_Method
    ON openalex.FacultyIdentityCandidate
    (
        FacultyId,
        CandidateAuthorOpenAlexId,
        DiscoveryMethod
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U')
      AND name = N'IX_FacultyIdentityCandidate_Faculty_Status_Score'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityCandidate_Faculty_Status_Score
    ON openalex.FacultyIdentityCandidate
    (
        FacultyId,
        EvidenceStatus,
        EvidenceScore DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U')
      AND name = N'IX_FacultyIdentityCandidate_AuthorOpenAlexShortId'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityCandidate_AuthorOpenAlexShortId
    ON openalex.FacultyIdentityCandidate
    (
        CandidateAuthorOpenAlexShortId
    );
END;

SELECT
    OBJECT_SCHEMA_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U')) AS SchemaName,
    OBJECT_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U')) AS TableName,
    COUNT(*) AS ExistingRows
FROM openalex.FacultyIdentityCandidate;
