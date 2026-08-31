/*
V3E PATCH017B11 - Five-Faculty Publication Candidate Execution Audit
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'openalex') IS NULL
    EXEC(N'CREATE SCHEMA openalex');

IF OBJECT_ID(N'openalex.FacultyPublicationCohortExecutionRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCohortExecutionRun
    (
        FacultyPublicationCohortExecutionRunId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FacultyPublicationCohortExecutionRun PRIMARY KEY,
        CohortName NVARCHAR(400) NOT NULL,
        ExpectedFacultyCount INT NOT NULL,
        ExpectedAcceptedAuthorDecisionCount INT NOT NULL,
        ExpectedRawSourceRowCount INT NOT NULL,
        ExpectedDeduplicatedCandidateCount INT NOT NULL,
        ExpectedDuplicateRawRowsRemoved INT NOT NULL,
        ExpectedExistingCandidateCount INT NOT NULL,
        MaxCandidates INT NOT NULL,
        ConfirmedExecution BIT NOT NULL,
        IngestionRunId BIGINT NULL,
        EnabledFacultyCount INT NULL,
        AcceptedAuthorDecisionCount INT NULL,
        RawSourceRowCount INT NULL,
        SelectedCandidateCount INT NULL,
        DuplicateRawRowsRemoved INT NULL,
        ExistingCandidateCount INT NULL,
        CandidateRowsInserted INT NULL,
        ValidationFailureCount INT NULL,
        PublicationCandidateRowsBefore INT NOT NULL,
        PublicationCandidateRowsAfter INT NULL,
        PublicationDecisionRowsBefore INT NOT NULL,
        PublicationDecisionRowsAfter INT NULL,
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

    CREATE INDEX IX_FacultyPublicationCohortExecutionRun_Cohort
        ON openalex.FacultyPublicationCohortExecutionRun
        (CohortName, FacultyPublicationCohortExecutionRunId DESC);
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCohortExecutionItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationCohortExecutionItem
    (
        FacultyPublicationCohortExecutionItemId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FacultyPublicationCohortExecutionItem PRIMARY KEY,
        FacultyPublicationCohortExecutionRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        FacultyAuthorMatchDecisionId BIGINT NOT NULL,
        AuthorOpenAlexId VARCHAR(128) NOT NULL,
        WorkId BIGINT NOT NULL,
        WorkOpenAlexId VARCHAR(128) NOT NULL,
        WorkTitle NVARCHAR(MAX) NULL,
        WorkAuthorshipId BIGINT NOT NULL,
        MatchMethod VARCHAR(40) NOT NULL,
        RawMatchCountForFacultyWork INT NOT NULL,
        ProposedMatchScore DECIMAL(9,4) NOT NULL,
        CandidateIdBefore BIGINT NULL,
        CandidateIdAfter BIGINT NULL,
        CandidateExecutionAction VARCHAR(50) NOT NULL,
        ExecutionStatus VARCHAR(30) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_FacultyPublicationCohortExecutionItem_Run
            FOREIGN KEY (FacultyPublicationCohortExecutionRunId)
            REFERENCES openalex.FacultyPublicationCohortExecutionRun
                (FacultyPublicationCohortExecutionRunId)
    );

    CREATE INDEX IX_FacultyPublicationCohortExecutionItem_Run
        ON openalex.FacultyPublicationCohortExecutionItem
        (FacultyPublicationCohortExecutionRunId, FacultyId, WorkId);
END;

SELECT
    ExecutionRunObjectId =
        OBJECT_ID(N'openalex.FacultyPublicationCohortExecutionRun'),
    ExecutionItemObjectId =
        OBJECT_ID(N'openalex.FacultyPublicationCohortExecutionItem'),
    SetupStatus = 'Ready';
