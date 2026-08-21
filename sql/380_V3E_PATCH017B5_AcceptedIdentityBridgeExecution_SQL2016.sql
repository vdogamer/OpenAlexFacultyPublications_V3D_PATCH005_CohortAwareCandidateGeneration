/*
V3E PATCH017B5 - Controlled Accepted-Identity Bridge Execution
SQL Server 2016-safe.

Purpose:
  Audit the controlled execution that converts selected PATCH017B4 preview rows
  into accepted faculty-author match candidates and decisions.

Safety boundary:
  - Requires one explicit completed PATCH017B4 preview run.
  - Does not create faculty-publication candidates.
  - Does not alter faculty-publication decisions.
  - Preserves legacy author-match constraint values while adding the provenance
    values required by the existing accepted-identity bridge implementation.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U') IS NULL
BEGIN
    RAISERROR('Missing PATCH017B4 preview tables. Run sql/378_V3E_PATCH017B4_AcceptedIdentityBridgePreview_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.FacultyAuthorMatchCandidate. Run the author review schema first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL
BEGIN
    RAISERROR('Missing openalex.FacultyAuthorMatchDecision. Run the author review schema first.', 16, 1);
    RETURN;
END;

/*
PATCH017B5 compatibility upgrade.

The bridge code has long used the provenance values below, but older databases may
still have check constraints created before those values existed. Rather than
replace the legacy allowed-value list, preserve each existing constraint definition
and OR in only the new bridge value.
*/
DECLARE @ConstraintName SYSNAME;
DECLARE @ConstraintDefinition NVARCHAR(MAX);
DECLARE @Sql NVARCHAR(MAX);

SET @ConstraintName = N'CK_FacultyAuthorMatchCandidate_CandidateSource';
SELECT @ConstraintDefinition = cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U')
  AND cc.name = @ConstraintName;

IF @ConstraintDefinition IS NULL
BEGIN
    RAISERROR('Missing CK_FacultyAuthorMatchCandidate_CandidateSource. Refusing PATCH017B5 compatibility upgrade because the legacy constraint could not be preserved.', 16, 1);
    RETURN;
END;

IF CHARINDEX(N'AcceptedIdentityDecision', @ConstraintDefinition) = 0
BEGIN
    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchCandidate DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
    EXEC sp_executesql @Sql;

    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchCandidate WITH CHECK ADD CONSTRAINT ' + QUOTENAME(@ConstraintName) +
        N' CHECK (' + @ConstraintDefinition + N' OR [CandidateSource] = ''AcceptedIdentityDecision'');';
    EXEC sp_executesql @Sql;

    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchCandidate CHECK CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
    EXEC sp_executesql @Sql;
END;

SET @ConstraintName = N'CK_FacultyAuthorMatchDecision_DecisionReason';
SET @ConstraintDefinition = NULL;
SELECT @ConstraintDefinition = cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U')
  AND cc.name = @ConstraintName;

IF @ConstraintDefinition IS NULL
BEGIN
    RAISERROR('Missing CK_FacultyAuthorMatchDecision_DecisionReason. Refusing PATCH017B5 compatibility upgrade because the legacy constraint could not be preserved.', 16, 1);
    RETURN;
END;

IF CHARINDEX(N'AcceptedIdentityDecision', @ConstraintDefinition) = 0
BEGIN
    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchDecision DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
    EXEC sp_executesql @Sql;

    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchDecision WITH CHECK ADD CONSTRAINT ' + QUOTENAME(@ConstraintName) +
        N' CHECK (' + @ConstraintDefinition + N' OR [DecisionReason] = ''AcceptedIdentityDecision'');';
    EXEC sp_executesql @Sql;

    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchDecision CHECK CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
    EXEC sp_executesql @Sql;
END;

SET @ConstraintName = N'CK_FacultyAuthorMatchDecision_DecisionSource';
SET @ConstraintDefinition = NULL;
SELECT @ConstraintDefinition = cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U')
  AND cc.name = @ConstraintName;

IF @ConstraintDefinition IS NULL
BEGIN
    RAISERROR('Missing CK_FacultyAuthorMatchDecision_DecisionSource. Refusing PATCH017B5 compatibility upgrade because the legacy constraint could not be preserved.', 16, 1);
    RETURN;
END;

IF CHARINDEX(N'IdentityReviewBridge', @ConstraintDefinition) = 0
BEGIN
    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchDecision DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
    EXEC sp_executesql @Sql;

    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchDecision WITH CHECK ADD CONSTRAINT ' + QUOTENAME(@ConstraintName) +
        N' CHECK (' + @ConstraintDefinition + N' OR [DecisionSource] = ''IdentityReviewBridge'');';
    EXEC sp_executesql @Sql;

    SET @Sql = N'ALTER TABLE openalex.FacultyAuthorMatchDecision CHECK CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
    EXEC sp_executesql @Sql;
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityBridgeExecutionRun
    (
        AcceptedIdentityBridgeExecutionRunId BIGINT IDENTITY(1,1) NOT NULL,
        SourceAcceptedIdentityBridgePreviewRunId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        MaxIdentities INT NOT NULL,
        ConfirmedBridge BIT NOT NULL,
        IngestionRunId BIGINT NULL,
        SelectedIdentityCount INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_Selected DEFAULT (0),
        CandidateRowsInserted INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_CandidateInserted DEFAULT (0),
        CandidateRowsUpdated INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_CandidateUpdated DEFAULT (0),
        DecisionRowsInserted INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_DecisionInserted DEFAULT (0),
        DecisionsAlreadyAccepted INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_AlreadyAccepted DEFAULT (0),
        MissingLocalAuthorCount INT NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_MissingAuthor DEFAULT (0),
        AuthorCandidateRowsBefore INT NULL,
        AuthorCandidateRowsAfter INT NULL,
        AuthorDecisionRowsBefore INT NULL,
        AuthorDecisionRowsAfter INT NULL,
        PublicationCandidateRowsBefore INT NULL,
        PublicationCandidateRowsAfter INT NULL,
        PublicationDecisionRowsBefore INT NULL,
        PublicationDecisionRowsAfter INT NULL,
        RunStatus VARCHAR(50) NOT NULL,
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(100) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgeExecutionRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcceptedIdentityBridgeExecutionRun PRIMARY KEY CLUSTERED (AcceptedIdentityBridgeExecutionRunId),
        CONSTRAINT FK_AIBridgeExecutionRun_PreviewRun FOREIGN KEY (SourceAcceptedIdentityBridgePreviewRunId)
            REFERENCES openalex.AcceptedIdentityBridgePreviewRun (AcceptedIdentityBridgePreviewRunId),
        CONSTRAINT CK_AIBridgeExecutionRun_MaxIdentities CHECK (MaxIdentities BETWEEN 1 AND 5),
        CONSTRAINT CK_AIBridgeExecutionRun_Confirmed CHECK (ConfirmedBridge = 1)
    );
END;

IF OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AcceptedIdentityBridgeExecutionItem
    (
        AcceptedIdentityBridgeExecutionItemId BIGINT IDENTITY(1,1) NOT NULL,
        AcceptedIdentityBridgeExecutionRunId BIGINT NOT NULL,
        SourceAcceptedIdentityBridgePreviewItemId BIGINT NOT NULL,
        FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
        FacultyIdentityCandidateSummaryId BIGINT NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NOT NULL,
        CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
        CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
        CandidateDisplayName NVARCHAR(512) NULL,
        CandidateOrcid NVARCHAR(256) NULL,
        LocalAuthorId BIGINT NULL,
        BridgeCandidateIdBefore BIGINT NULL,
        BridgeCandidateIdAfter BIGINT NOT NULL,
        AcceptedAuthorDecisionIdBefore BIGINT NULL,
        AcceptedAuthorDecisionIdAfter BIGINT NOT NULL,
        CandidateExecutionAction VARCHAR(100) NOT NULL,
        DecisionExecutionAction VARCHAR(100) NOT NULL,
        ExecutionStatus VARCHAR(50) NOT NULL,
        WarningText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AIBridgeExecutionItem_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AcceptedIdentityBridgeExecutionItem PRIMARY KEY CLUSTERED (AcceptedIdentityBridgeExecutionItemId),
        CONSTRAINT FK_AIBridgeExecutionItem_Run FOREIGN KEY (AcceptedIdentityBridgeExecutionRunId)
            REFERENCES openalex.AcceptedIdentityBridgeExecutionRun (AcceptedIdentityBridgeExecutionRunId),
        CONSTRAINT FK_AIBridgeExecutionItem_PreviewItem FOREIGN KEY (SourceAcceptedIdentityBridgePreviewItemId)
            REFERENCES openalex.AcceptedIdentityBridgePreviewItem (AcceptedIdentityBridgePreviewItemId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U')
      AND name = N'IX_AIBridgeExecutionRun_SourceFaculty'
)
BEGIN
    CREATE INDEX IX_AIBridgeExecutionRun_SourceFaculty
    ON openalex.AcceptedIdentityBridgeExecutionRun
    (
        SourceAcceptedIdentityBridgePreviewRunId,
        FacultyId,
        RunStatus,
        AcceptedIdentityBridgeExecutionRunId DESC
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U')
      AND name = N'IX_AIBridgeExecutionItem_RunAuthor'
)
BEGIN
    CREATE INDEX IX_AIBridgeExecutionItem_RunAuthor
    ON openalex.AcceptedIdentityBridgeExecutionItem
    (
        AcceptedIdentityBridgeExecutionRunId,
        CandidateAuthorOpenAlexId,
        ExecutionStatus
    );
END;

SELECT
    ExecutionRunObjectId = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U'),
    ExecutionItemObjectId = OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U'),
    CandidateSourceAllowsAcceptedIdentityDecision = CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U')
          AND name = N'CK_FacultyAuthorMatchCandidate_CandidateSource'
          AND CHARINDEX(N'AcceptedIdentityDecision', definition) > 0
    ) THEN 1 ELSE 0 END,
    DecisionReasonAllowsAcceptedIdentityDecision = CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U')
          AND name = N'CK_FacultyAuthorMatchDecision_DecisionReason'
          AND CHARINDEX(N'AcceptedIdentityDecision', definition) > 0
    ) THEN 1 ELSE 0 END,
    DecisionSourceAllowsIdentityReviewBridge = CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U')
          AND name = N'CK_FacultyAuthorMatchDecision_DecisionSource'
          AND CHARINDEX(N'IdentityReviewBridge', definition) > 0
    ) THEN 1 ELSE 0 END;
