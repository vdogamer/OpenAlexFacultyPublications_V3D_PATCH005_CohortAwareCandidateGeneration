/*
V3E PATCH004 - Faculty Identity Candidate Summary
SQL Server 2016-safe.

Purpose:
  Materialize one review-facing summary row per Faculty + candidate OpenAlex author ID.
  These rows are triage summaries only. They are not accepted or rejected identities.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyIdentityCandidateSummary
    (
        FacultyIdentityCandidateSummaryId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        CandidateDisplayName NVARCHAR(512) NULL,
        CandidateOrcid NVARCHAR(256) NULL,
        WorksCount INT NULL,
        CitedByCount INT NULL,
        DiscoveryMethodCount INT NOT NULL,
        DiscoveryMethods NVARCHAR(MAX) NULL,
        BestIdentityEvidenceScore DECIMAL(9,4) NOT NULL,
        BestIdentityEvidenceStatus VARCHAR(50) NULL,
        BestCandidateType VARCHAR(50) NULL,
        ExpandedWorkRows INT NOT NULL,
        DistinctExpandedWorks INT NOT NULL,
        ScoredWorkRows INT NOT NULL,
        StrongWorkEvidenceRows INT NOT NULL,
        ModerateWorkEvidenceRows INT NOT NULL,
        WeakWorkEvidenceRows INT NOT NULL,
        ConflictingWorkEvidenceRows INT NOT NULL,
        AverageWorkEvidenceScore DECIMAL(9,4) NULL,
        BestWorkEvidenceScore DECIMAL(9,4) NULL,
        EarliestPublicationYear INT NULL,
        LatestPublicationYear INT NULL,
        IdentityReviewTriageStatus VARCHAR(50) NOT NULL,
        ReviewPriority INT NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        EvidenceJson NVARCHAR(MAX) NULL,
        IngestionRunId BIGINT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidateSummary_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityCandidateSummary_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyIdentityCandidateSummary PRIMARY KEY CLUSTERED (FacultyIdentityCandidateSummaryId),
        CONSTRAINT CK_FacultyIdentityCandidateSummary_TriageStatus CHECK (IdentityReviewTriageStatus IN ('LikelyIdentity', 'PossibleSplitProfile', 'NeedsManualReview', 'WeakCandidate', 'LikelyFalsePositive'))
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U')
      AND name = N'UX_FacultyIdentityCandidateSummary_Faculty_Author'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyIdentityCandidateSummary_Faculty_Author
    ON openalex.FacultyIdentityCandidateSummary
    (
        FacultyId,
        CandidateAuthorOpenAlexId
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U')
      AND name = N'IX_FacultyIdentityCandidateSummary_Faculty_Priority'
)
BEGIN
    CREATE INDEX IX_FacultyIdentityCandidateSummary_Faculty_Priority
    ON openalex.FacultyIdentityCandidateSummary
    (
        FacultyId,
        ReviewPriority,
        BestIdentityEvidenceScore DESC,
        BestWorkEvidenceScore DESC
    );
END;

SELECT
    OBJECT_SCHEMA_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U')) AS SchemaName,
    OBJECT_NAME(OBJECT_ID(N'openalex.FacultyIdentityCandidateSummary', N'U')) AS TableName,
    COUNT(*) AS ExistingRows
FROM openalex.FacultyIdentityCandidateSummary;
