/*
V3E PATCH017B10A - Accepted-Identity Work Promotion Execution Audit
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'openalex') IS NULL
    EXEC(N'CREATE SCHEMA openalex');

IF OBJECT_ID(N'openalex.AcceptedIdentityWorkPromotionRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityWorkPromotionRun
    (
        AcceptedIdentityWorkPromotionRunId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AcceptedIdentityWorkPromotionRun PRIMARY KEY,
        CohortName NVARCHAR(400) NOT NULL,
        ExpectedFacultyCount INT NOT NULL,
        ExpectedMissingWorkCount INT NOT NULL,
        MaxWorks INT NOT NULL,
        ConfirmedPromotion BIT NOT NULL,
        IngestionRunId BIGINT NULL,
        SelectedWorkCount INT NULL,
        InsertedWorkCount INT NULL,
        UpdatedWorkCount INT NULL,
        AuthorshipRows INT NULL,
        AuthorshipInstitutionRows INT NULL,
        MatchedInstitutionRows INT NULL,
        FailedItemCount INT NULL,
        WorkRowsBefore INT NOT NULL,
        WorkRowsAfter INT NULL,
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

    CREATE INDEX IX_AcceptedIdentityWorkPromotionRun_Cohort
        ON openalex.AcceptedIdentityWorkPromotionRun
        (CohortName, AcceptedIdentityWorkPromotionRunId DESC);
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityWorkPromotionItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityWorkPromotionItem
    (
        AcceptedIdentityWorkPromotionItemId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AcceptedIdentityWorkPromotionItem PRIMARY KEY,
        AcceptedIdentityWorkPromotionRunId BIGINT NOT NULL,
        PilotOrder INT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        AuthorOpenAlexId VARCHAR(128) NOT NULL,
        SourceFacultyIdentityCandidateWorkId BIGINT NOT NULL,
        WorkOpenAlexId VARCHAR(128) NOT NULL,
        WorkDisplayName NVARCHAR(MAX) NULL,
        WorkIdAfter BIGINT NULL,
        PromotionStatus VARCHAR(30) NOT NULL,
        AuthorshipRows INT NULL,
        AuthorshipInstitutionRows INT NULL,
        MatchedInstitutionRows INT NULL,
        ErrorText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_AcceptedIdentityWorkPromotionItem_Run
            FOREIGN KEY (AcceptedIdentityWorkPromotionRunId)
            REFERENCES openalex.AcceptedIdentityWorkPromotionRun
                (AcceptedIdentityWorkPromotionRunId)
    );

    CREATE INDEX IX_AcceptedIdentityWorkPromotionItem_Run
        ON openalex.AcceptedIdentityWorkPromotionItem
        (AcceptedIdentityWorkPromotionRunId, AcceptedIdentityWorkPromotionItemId);

    CREATE INDEX IX_AcceptedIdentityWorkPromotionItem_Work
        ON openalex.AcceptedIdentityWorkPromotionItem
        (WorkOpenAlexId);
END;

SELECT
    PromotionRunObjectId =
        OBJECT_ID(N'openalex.AcceptedIdentityWorkPromotionRun'),
    PromotionItemObjectId =
        OBJECT_ID(N'openalex.AcceptedIdentityWorkPromotionItem'),
    SetupStatus = 'Ready';
