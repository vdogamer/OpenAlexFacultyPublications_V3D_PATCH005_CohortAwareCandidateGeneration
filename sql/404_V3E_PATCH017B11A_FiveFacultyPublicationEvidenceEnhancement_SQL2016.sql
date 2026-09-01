/*
V3E PATCH017B11A - Five-Faculty Publication Evidence Enhancement Audit
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'openalex') IS NULL
    EXEC(N'CREATE SCHEMA openalex');

IF OBJECT_ID(N'openalex.FacultyPublicationCohortEvidenceEnhancementRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCohortEvidenceEnhancementRun
    (
        FacultyPublicationCohortEvidenceEnhancementRunId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FacultyPublicationCohortEvidenceEnhancementRun PRIMARY KEY,
        CohortName NVARCHAR(400) NOT NULL,
        SourceCandidateIngestionRunId BIGINT NOT NULL,
        ExpectedCandidateCount INT NOT NULL,
        ConfirmedEnhancement BIT NOT NULL,
        CandidateRowsEvaluated INT NULL,
        CandidateRowsEnhanced INT NULL,
        RowsWithAffiliatedInstitutionEvidence INT NULL,
        RowsWithKnownCoauthorEvidence INT NULL,
        CandidateRowsTotalBefore INT NOT NULL,
        CandidateRowsTotalAfter INT NULL,
        PublicationDecisionRowsBefore INT NOT NULL,
        PublicationDecisionRowsAfter INT NULL,
        SourceCandidateRowsBefore INT NOT NULL,
        SourceCandidateRowsAfter INT NULL,
        SourceCandidatesWithEvidenceBefore INT NOT NULL,
        SourceCandidatesWithEvidenceAfter INT NULL,
        RunStatus VARCHAR(30) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion VARCHAR(50) NOT NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL
    );

    CREATE INDEX IX_FacultyPublicationCohortEvidenceEnhancementRun_Cohort
        ON openalex.FacultyPublicationCohortEvidenceEnhancementRun
        (CohortName, FacultyPublicationCohortEvidenceEnhancementRunId DESC);
END;

SELECT
    EvidenceEnhancementRunObjectId =
        OBJECT_ID(N'openalex.FacultyPublicationCohortEvidenceEnhancementRun'),
    SetupStatus = 'Ready';
