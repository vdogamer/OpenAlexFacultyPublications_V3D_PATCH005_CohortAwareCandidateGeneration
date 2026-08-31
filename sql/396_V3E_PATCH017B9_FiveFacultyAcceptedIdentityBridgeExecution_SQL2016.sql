/*
V3E PATCH017B9 - Five-Faculty Accepted-Identity Bridge Execution Audit
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'openalex') IS NULL
    EXEC(N'CREATE SCHEMA openalex');

IF OBJECT_ID(N'openalex.AcceptedIdentityCohortBridgeExecutionRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityCohortBridgeExecutionRun
    (
        AcceptedIdentityCohortBridgeExecutionRunId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AcceptedIdentityCohortBridgeExecutionRun PRIMARY KEY,
        CohortName NVARCHAR(400) NOT NULL,
        ExpectedFacultyCount INT NOT NULL,
        MaxIdentities INT NOT NULL,
        ConfirmedBridge BIT NOT NULL,
        IngestionRunId BIGINT NULL,
        EnabledFacultyCount INT NULL,
        FacultyWithAcceptedIdentity INT NULL,
        SelectedAcceptedIdentityCount INT NULL,
        CandidateRowsInserted INT NULL,
        CandidateRowsUpdated INT NULL,
        DecisionRowsInserted INT NULL,
        DecisionsAlreadyAccepted INT NULL,
        MissingLocalAuthorCount INT NULL,
        AuthorCandidateRowsBefore INT NOT NULL,
        AuthorCandidateRowsAfter INT NULL,
        AuthorDecisionRowsBefore INT NOT NULL,
        AuthorDecisionRowsAfter INT NULL,
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

    CREATE INDEX IX_AcceptedIdentityCohortBridgeExecutionRun_Cohort
        ON openalex.AcceptedIdentityCohortBridgeExecutionRun
        (CohortName, AcceptedIdentityCohortBridgeExecutionRunId DESC);
END;

SELECT
    ExecutionRunObjectId =
        OBJECT_ID(N'openalex.AcceptedIdentityCohortBridgeExecutionRun'),
    SetupStatus = 'Ready';
