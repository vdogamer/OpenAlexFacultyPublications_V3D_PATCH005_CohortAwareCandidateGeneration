/*
V3E PATCH005 - Faculty Identity Candidate Review Decision Table
SQL Server 2016-safe.

Purpose:
  Store human review decisions for identity candidates shown in the Identity Review UI.

Important:
  These decisions do NOT automatically create publication candidates.
  These decisions do NOT automatically accept publications.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyIdentityCandidateDecision
    (
        FacultyIdentityCandidateDecisionId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        DecisionStatus VARCHAR(50) NOT NULL,
        ReviewNotes NVARCHAR(MAX) NULL,
        ReviewerName NVARCHAR(256) NULL,
        IsCurrent BIT NOT NULL CONSTRAINT DF_FacultyIdentityCandidateDecision_IsCurrent DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidateDecision_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidateDecision_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyIdentityCandidateDecision PRIMARY KEY CLUSTERED (FacultyIdentityCandidateDecisionId),
        CONSTRAINT CK_FacultyIdentityCandidateDecision_Status CHECK (DecisionStatus IN ('AcceptedIdentity', 'RejectedIdentity', 'NeedsMoreEvidence'))
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U')
      AND name = N'UX_FacultyIdentityCandidateDecision_Current'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyIdentityCandidateDecision_Current
    ON openalex.FacultyIdentityCandidateDecision
    (
        FacultyId,
        CandidateAuthorOpenAlexId
    )
    WHERE IsCurrent = 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U')
      AND name = N'IX_FacultyIdentityCandidateDecision_Faculty_Status'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityCandidateDecision_Faculty_Status
    ON openalex.FacultyIdentityCandidateDecision
    (
        FacultyId,
        DecisionStatus,
        IsCurrent
    );
END;

SELECT
    OBJECT_SCHEMA_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U')) AS SchemaName,
    OBJECT_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U')) AS TableName,
    COUNT(*) AS ExistingRows
FROM openalex.FacultyIdentityCandidateDecision;
