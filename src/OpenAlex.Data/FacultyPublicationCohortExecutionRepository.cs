using System.Data;
using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class FacultyPublicationCohortExecutionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyPublicationCohortExecutionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FacultyPublicationCohortExecutionSafetyCounts> GetSafetyCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    PublicationCandidateRows = (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate),
    PublicationDecisionRows = (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision);";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Unable to read publication cohort execution safety counts.");
        }

        return new FacultyPublicationCohortExecutionSafetyCounts
        {
            PublicationCandidateRows = reader.GetInt32(0),
            PublicationDecisionRows = reader.GetInt32(1)
        };
    }

    public async Task<long> StartRunAsync(
        FacultyPublicationCohortExecutionOptionsSnapshot expected,
        FacultyPublicationCohortExecutionSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureAuditTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.FacultyPublicationCohortExecutionRun
(
    CohortName,
    ExpectedFacultyCount,
    ExpectedAcceptedAuthorDecisionCount,
    ExpectedRawSourceRowCount,
    ExpectedDeduplicatedCandidateCount,
    ExpectedDuplicateRawRowsRemoved,
    ExpectedExistingCandidateCount,
    MaxCandidates,
    ConfirmedExecution,
    PublicationCandidateRowsBefore,
    PublicationDecisionRowsBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.FacultyPublicationCohortExecutionRunId
VALUES
(
    @CohortName,
    @ExpectedFacultyCount,
    @ExpectedAcceptedAuthorDecisionCount,
    @ExpectedRawSourceRowCount,
    @ExpectedDeduplicatedCandidateCount,
    @ExpectedDuplicateRawRowsRemoved,
    @ExpectedExistingCandidateCount,
    @MaxCandidates,
    1,
    @PublicationCandidateRowsBefore,
    @PublicationDecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@CohortName", expected.CohortName);
        command.Parameters.AddWithValue("@ExpectedFacultyCount", expected.ExpectedFacultyCount);
        command.Parameters.AddWithValue("@ExpectedAcceptedAuthorDecisionCount", expected.ExpectedAcceptedAuthorDecisionCount);
        command.Parameters.AddWithValue("@ExpectedRawSourceRowCount", expected.ExpectedRawSourceRowCount);
        command.Parameters.AddWithValue("@ExpectedDeduplicatedCandidateCount", expected.ExpectedDeduplicatedCandidateCount);
        command.Parameters.AddWithValue("@ExpectedDuplicateRawRowsRemoved", expected.ExpectedDuplicateRawRowsRemoved);
        command.Parameters.AddWithValue("@ExpectedExistingCandidateCount", expected.ExpectedExistingCandidateCount);
        command.Parameters.AddWithValue("@MaxCandidates", expected.MaxCandidates);
        command.Parameters.AddWithValue("@PublicationCandidateRowsBefore", safetyBefore.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsBefore", safetyBefore.PublicationDecisionRows);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task AttachIngestionRunAsync(
        long executionRunId,
        long ingestionRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCohortExecutionRun
SET IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCohortExecutionRunId = @ExecutionRunId
  AND RunStatus = 'Running';";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Unable to attach ingestion run to PATCH017B11 execution.");
        }
    }

    public async Task<FacultyPublicationCohortExecutionResult> ExecuteAsync(
        long executionRunId,
        FacultyPublicationCohortExecutionOptionsSnapshot expected,
        long ingestionRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyPublicationCohortExecutionRun WITH (UPDLOCK, HOLDLOCK)
    WHERE FacultyPublicationCohortExecutionRunId = @ExecutionRunId
      AND CohortName = @CohortName
      AND ConfirmedExecution = 1
      AND RunStatus = 'Running'
)
BEGIN
    RAISERROR('PATCH017B11 execution run is not in the expected Running state.', 16, 1);
    RETURN;
END;

DECLARE @CandidateRowsAtTransactionStart INT =
(
    SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate WITH (HOLDLOCK)
);
DECLARE @DecisionRowsAtTransactionStart INT =
(
    SELECT COUNT(*) FROM openalex.FacultyPublicationDecision WITH (HOLDLOCK)
);

DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

IF @EnabledFacultyCount <> @ExpectedFacultyCount
BEGIN
    RAISERROR('Enabled cohort faculty count changed after PATCH017B10 preview.', 16, 1);
    RETURN;
END;

DECLARE @AcceptedAuthorDecisionCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyAuthorMatchDecision d
    WHERE d.DecisionStatus = 'Accepted'
      AND d.IsCurrent = 1
      AND EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.CohortName = @CohortName
            AND pc.IsEnabled = 1
            AND pc.FacultyId = d.FacultyId
      )
);

IF @AcceptedAuthorDecisionCount <> @ExpectedAcceptedAuthorDecisionCount
BEGIN
    RAISERROR('Accepted author decision count changed after PATCH017B10 preview.', 16, 1);
    RETURN;
END;

DECLARE @Raw TABLE
(
    PilotOrder INT NOT NULL,
    FacultyAuthorMatchDecisionId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NOT NULL,
    AuthorOpenAlexId VARCHAR(128) NOT NULL,
    AuthorDisplayName NVARCHAR(512) NULL,
    AuthorOrcid NVARCHAR(256) NULL,
    WorkId BIGINT NOT NULL,
    WorkOpenAlexId VARCHAR(128) NOT NULL,
    WorkOpenAlexShortId VARCHAR(64) NULL,
    WorkTitle NVARCHAR(MAX) NULL,
    WorkDoi NVARCHAR(512) NULL,
    PublicationYear INT NULL,
    PublicationDate DATE NULL,
    WorkType VARCHAR(100) NULL,
    WorkAuthorshipId BIGINT NOT NULL,
    OrdinalPosition INT NULL,
    IsCorresponding BIT NULL,
    AuthorshipInstitutionCount INT NOT NULL,
    MatchingInstitutionCount INT NOT NULL,
    MatchMethod VARCHAR(40) NOT NULL,
    ExistingCandidateId BIGINT NULL
);

INSERT INTO @Raw
(
    PilotOrder,
    FacultyAuthorMatchDecisionId,
    FacultyId,
    FacultyDisplayName,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    WorkAuthorshipId,
    OrdinalPosition,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    MatchMethod,
    ExistingCandidateId
)
SELECT
    pc.PilotOrder,
    d.FacultyAuthorMatchDecisionId,
    d.FacultyId,
    f.DisplayName,
    d.AuthorOpenAlexId,
    d.AuthorDisplayName,
    d.AuthorOrcid,
    w.WorkId,
    w.OpenAlexId,
    w.OpenAlexShortId,
    COALESCE(w.Title, w.DisplayName),
    w.Doi,
    w.PublicationYear,
    w.PublicationDate,
    w.WorkType,
    wa.WorkAuthorshipId,
    wa.OrdinalPosition,
    wa.IsCorresponding,
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        INNER JOIN openalex.Institution i
            ON i.OpenAlexId = wai.InstitutionOpenAlexId
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    CASE
        WHEN wa.AuthorOpenAlexId = d.AuthorOpenAlexId THEN 'ExactAuthorOpenAlexId'
        WHEN d.AuthorOrcid IS NOT NULL
         AND wa.AuthorOrcid IS NOT NULL
         AND wa.AuthorOrcid = d.AuthorOrcid THEN 'ExactOrcid'
        ELSE 'Unexpected'
    END,
    (
        SELECT TOP (1) c.FacultyPublicationCandidateId
        FROM openalex.FacultyPublicationCandidate c WITH (UPDLOCK, HOLDLOCK)
        WHERE c.FacultyId = d.FacultyId
          AND c.WorkId = w.WorkId
          AND c.CandidateSource = 'AcceptedAuthorMatch'
        ORDER BY c.FacultyPublicationCandidateId DESC
    )
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
INNER JOIN openalex.FacultyAuthorMatchDecision d
    ON d.FacultyId = pc.FacultyId
   AND d.DecisionStatus = 'Accepted'
   AND d.IsCurrent = 1
INNER JOIN openalex.WorkAuthorship wa
    ON
    (
        wa.AuthorOpenAlexId = d.AuthorOpenAlexId
        OR
        (
            d.AuthorOrcid IS NOT NULL
            AND wa.AuthorOrcid IS NOT NULL
            AND wa.AuthorOrcid = d.AuthorOrcid
        )
    )
INNER JOIN openalex.Work w
    ON w.WorkId = wa.WorkId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1;

DECLARE @RawSourceRowCount INT = (SELECT COUNT(*) FROM @Raw);

IF @RawSourceRowCount <> @ExpectedRawSourceRowCount
BEGIN
    RAISERROR('Raw publication source row count changed after PATCH017B10 preview.', 16, 1);
    RETURN;
END;

DECLARE @Selected TABLE
(
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NOT NULL,
    FacultyAuthorMatchDecisionId BIGINT NOT NULL,
    AuthorOpenAlexId VARCHAR(128) NOT NULL,
    AuthorDisplayName NVARCHAR(512) NULL,
    AuthorOrcid NVARCHAR(256) NULL,
    WorkId BIGINT NOT NULL,
    WorkOpenAlexId VARCHAR(128) NOT NULL,
    WorkOpenAlexShortId VARCHAR(64) NULL,
    WorkTitle NVARCHAR(MAX) NULL,
    WorkDoi NVARCHAR(512) NULL,
    PublicationYear INT NULL,
    PublicationDate DATE NULL,
    WorkType VARCHAR(100) NULL,
    WorkAuthorshipId BIGINT NOT NULL,
    IsCorresponding BIT NULL,
    AuthorshipInstitutionCount INT NOT NULL,
    MatchingInstitutionCount INT NOT NULL,
    MatchMethod VARCHAR(40) NOT NULL,
    RawMatchCountForFacultyWork INT NOT NULL,
    ProposedMatchScore DECIMAL(9,4) NOT NULL,
    ExistingCandidateId BIGINT NULL,
    CandidateIdAfter BIGINT NULL,
    PRIMARY KEY (FacultyId, WorkId)
);

;WITH Ranked AS
(
    SELECT
        r.*,
        RawMatchCountForFacultyWork =
            COUNT(*) OVER (PARTITION BY r.FacultyId, r.WorkId),
        SelectionRank =
            ROW_NUMBER() OVER
            (
                PARTITION BY r.FacultyId, r.WorkId
                ORDER BY
                    CASE WHEN r.MatchMethod = 'ExactAuthorOpenAlexId' THEN 0 ELSE 1 END,
                    CASE WHEN r.IsCorresponding = 1 THEN 0 ELSE 1 END,
                    r.MatchingInstitutionCount DESC,
                    r.AuthorshipInstitutionCount DESC,
                    r.FacultyAuthorMatchDecisionId DESC,
                    r.WorkAuthorshipId
            )
    FROM @Raw r
)
INSERT INTO @Selected
(
    FacultyId,
    FacultyDisplayName,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    WorkAuthorshipId,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    MatchMethod,
    RawMatchCountForFacultyWork,
    ProposedMatchScore,
    ExistingCandidateId
)
SELECT TOP (@MaxCandidates)
    FacultyId,
    FacultyDisplayName,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    WorkAuthorshipId,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    MatchMethod,
    RawMatchCountForFacultyWork,
    CAST
    (
        CASE
            WHEN
                80
                + CASE WHEN IsCorresponding = 1 THEN 5 ELSE 0 END
                + CASE WHEN MatchingInstitutionCount > 0 THEN 10 ELSE 0 END
                + CASE WHEN AuthorshipInstitutionCount > 0 THEN 5 ELSE 0 END
                > 100
                THEN 100
            ELSE
                80
                + CASE WHEN IsCorresponding = 1 THEN 5 ELSE 0 END
                + CASE WHEN MatchingInstitutionCount > 0 THEN 10 ELSE 0 END
                + CASE WHEN AuthorshipInstitutionCount > 0 THEN 5 ELSE 0 END
        END
        AS DECIMAL(9,4)
    ),
    ExistingCandidateId
FROM Ranked
WHERE SelectionRank = 1
ORDER BY FacultyDisplayName, PublicationYear DESC, WorkId DESC;

DECLARE @SelectedCandidateCount INT = (SELECT COUNT(*) FROM @Selected);
DECLARE @DuplicateRawRowsRemoved INT = @RawSourceRowCount - @SelectedCandidateCount;
DECLARE @ExistingCandidateCount INT =
(
    SELECT COUNT(*) FROM @Selected WHERE ExistingCandidateId IS NOT NULL
);

IF @SelectedCandidateCount <> @ExpectedDeduplicatedCandidateCount
BEGIN
    RAISERROR('Deduplicated publication candidate count changed after PATCH017B10 preview.', 16, 1);
    RETURN;
END;

IF @DuplicateRawRowsRemoved <> @ExpectedDuplicateRawRowsRemoved
BEGIN
    RAISERROR('Duplicate raw row count changed after PATCH017B10 preview.', 16, 1);
    RETURN;
END;

IF @ExistingCandidateCount <> @ExpectedExistingCandidateCount
BEGIN
    RAISERROR('Existing publication candidate count changed after PATCH017B10 preview.', 16, 1);
    RETURN;
END;

IF @SelectedCandidateCount > @MaxCandidates
BEGIN
    RAISERROR('Selected publication candidate count exceeds MaxCandidates.', 16, 1);
    RETURN;
END;

DECLARE @ValidationFailureCount INT = 0;

SELECT @ValidationFailureCount = COUNT(*)
FROM @Selected s
WHERE NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyAuthorMatchDecision d
    WHERE d.FacultyAuthorMatchDecisionId = s.FacultyAuthorMatchDecisionId
      AND d.FacultyId = s.FacultyId
      AND d.AuthorOpenAlexId = s.AuthorOpenAlexId
      AND d.DecisionStatus = 'Accepted'
      AND d.IsCurrent = 1
)
OR NOT EXISTS
(
    SELECT 1
    FROM openalex.Work w
    WHERE w.WorkId = s.WorkId
      AND w.OpenAlexId = s.WorkOpenAlexId
)
OR NOT EXISTS
(
    SELECT 1
    FROM openalex.WorkAuthorship wa
    WHERE wa.WorkAuthorshipId = s.WorkAuthorshipId
      AND wa.WorkId = s.WorkId
      AND
      (
          wa.AuthorOpenAlexId = s.AuthorOpenAlexId
          OR
          (
              s.AuthorOrcid IS NOT NULL
              AND wa.AuthorOrcid IS NOT NULL
              AND wa.AuthorOrcid = s.AuthorOrcid
          )
      )
);

IF @ValidationFailureCount > 0
BEGIN
    RAISERROR('PATCH017B11 revalidation failed for one or more selected publication candidates.', 16, 1);
    RETURN;
END;

INSERT INTO openalex.FacultyPublicationCandidate
(
    FacultyId,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    CandidateSource,
    CandidateStatus,
    MatchScore,
    EvidenceSummary,
    EvidenceJson,
    IngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    s.FacultyId,
    s.WorkId,
    s.WorkOpenAlexId,
    s.WorkOpenAlexShortId,
    s.WorkTitle,
    s.WorkDoi,
    s.PublicationYear,
    s.PublicationDate,
    s.WorkType,
    s.FacultyAuthorMatchDecisionId,
    s.AuthorOpenAlexId,
    s.AuthorDisplayName,
    s.AuthorOrcid,
    'AcceptedAuthorMatch',
    'PendingReview',
    s.ProposedMatchScore,
    'PATCH017B11 controlled five-faculty execution. MatchMethod=' + s.MatchMethod +
        '; RawMatchCount=' + CONVERT(NVARCHAR(20), s.RawMatchCountForFacultyWork) +
        '; AuthorshipInstitutionCount=' + CONVERT(NVARCHAR(20), s.AuthorshipInstitutionCount) +
        '; MatchingInstitutionCount=' + CONVERT(NVARCHAR(20), s.MatchingInstitutionCount) + '.',
    (
        SELECT
            s.FacultyAuthorMatchDecisionId AS FacultyAuthorMatchDecisionId,
            s.FacultyId AS FacultyId,
            s.AuthorOpenAlexId AS AuthorOpenAlexId,
            s.WorkId AS WorkId,
            s.WorkOpenAlexId AS WorkOpenAlexId,
            s.WorkAuthorshipId AS WorkAuthorshipId,
            s.MatchMethod AS MatchMethod,
            s.RawMatchCountForFacultyWork AS RawMatchCountForFacultyWork,
            s.IsCorresponding AS IsCorresponding,
            s.AuthorshipInstitutionCount AS AuthorshipInstitutionCount,
            s.MatchingInstitutionCount AS MatchingInstitutionCount,
            s.ProposedMatchScore AS MatchScore
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ),
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Selected s
WHERE s.ExistingCandidateId IS NULL;

DECLARE @CandidateRowsInserted INT = @@ROWCOUNT;

UPDATE s
SET CandidateIdAfter =
(
    SELECT TOP (1) c.FacultyPublicationCandidateId
    FROM openalex.FacultyPublicationCandidate c
    WHERE c.FacultyId = s.FacultyId
      AND c.WorkId = s.WorkId
      AND c.CandidateSource = 'AcceptedAuthorMatch'
    ORDER BY c.FacultyPublicationCandidateId DESC
)
FROM @Selected s;

IF EXISTS (SELECT 1 FROM @Selected WHERE CandidateIdAfter IS NULL)
BEGIN
    RAISERROR('PATCH017B11 could not verify a publication candidate after insertion.', 16, 1);
    RETURN;
END;

INSERT INTO openalex.FacultyPublicationCohortExecutionItem
(
    FacultyPublicationCohortExecutionRunId,
    FacultyId,
    FacultyDisplayName,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    WorkAuthorshipId,
    MatchMethod,
    RawMatchCountForFacultyWork,
    ProposedMatchScore,
    CandidateIdBefore,
    CandidateIdAfter,
    CandidateExecutionAction,
    ExecutionStatus,
    CreatedAtUtc
)
SELECT
    @ExecutionRunId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.FacultyAuthorMatchDecisionId,
    s.AuthorOpenAlexId,
    s.WorkId,
    s.WorkOpenAlexId,
    s.WorkTitle,
    s.WorkAuthorshipId,
    s.MatchMethod,
    s.RawMatchCountForFacultyWork,
    s.ProposedMatchScore,
    s.ExistingCandidateId,
    s.CandidateIdAfter,
    CASE WHEN s.ExistingCandidateId IS NULL THEN 'InsertedPendingReview'
         ELSE 'AlreadyExistsNoChange' END,
    'Completed',
    SYSUTCDATETIME()
FROM @Selected s;

DECLARE @CandidateRowsAtTransactionEnd INT =
(
    SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate
);
DECLARE @DecisionRowsAtTransactionEnd INT =
(
    SELECT COUNT(*) FROM openalex.FacultyPublicationDecision
);

IF @CandidateRowsAtTransactionEnd <> @CandidateRowsAtTransactionStart + @CandidateRowsInserted
   OR @DecisionRowsAtTransactionEnd <> @DecisionRowsAtTransactionStart
BEGIN
    RAISERROR('PATCH017B11 safety verification failed; candidate/decision counts changed unexpectedly.', 16, 1);
    RETURN;
END;

SELECT
    EnabledFacultyCount = @EnabledFacultyCount,
    AcceptedAuthorDecisionCount = @AcceptedAuthorDecisionCount,
    RawSourceRowCount = @RawSourceRowCount,
    SelectedCandidateCount = @SelectedCandidateCount,
    DuplicateRawRowsRemoved = @DuplicateRawRowsRemoved,
    ExistingCandidateCount = @ExistingCandidateCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    ValidationFailureCount = @ValidationFailureCount;";

            command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
            command.Parameters.AddWithValue("@CohortName", expected.CohortName);
            command.Parameters.AddWithValue("@ExpectedFacultyCount", expected.ExpectedFacultyCount);
            command.Parameters.AddWithValue("@ExpectedAcceptedAuthorDecisionCount", expected.ExpectedAcceptedAuthorDecisionCount);
            command.Parameters.AddWithValue("@ExpectedRawSourceRowCount", expected.ExpectedRawSourceRowCount);
            command.Parameters.AddWithValue("@ExpectedDeduplicatedCandidateCount", expected.ExpectedDeduplicatedCandidateCount);
            command.Parameters.AddWithValue("@ExpectedDuplicateRawRowsRemoved", expected.ExpectedDuplicateRawRowsRemoved);
            command.Parameters.AddWithValue("@ExpectedExistingCandidateCount", expected.ExpectedExistingCandidateCount);
            command.Parameters.AddWithValue("@MaxCandidates", expected.MaxCandidates);
            command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);

            FacultyPublicationCohortExecutionResult result;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("PATCH017B11 execution did not return a result row.");
                }

                result = new FacultyPublicationCohortExecutionResult
                {
                    EnabledFacultyCount = reader.GetInt32(reader.GetOrdinal("EnabledFacultyCount")),
                    AcceptedAuthorDecisionCount = reader.GetInt32(reader.GetOrdinal("AcceptedAuthorDecisionCount")),
                    RawSourceRowCount = reader.GetInt32(reader.GetOrdinal("RawSourceRowCount")),
                    SelectedCandidateCount = reader.GetInt32(reader.GetOrdinal("SelectedCandidateCount")),
                    DuplicateRawRowsRemoved = reader.GetInt32(reader.GetOrdinal("DuplicateRawRowsRemoved")),
                    ExistingCandidateCount = reader.GetInt32(reader.GetOrdinal("ExistingCandidateCount")),
                    CandidateRowsInserted = reader.GetInt32(reader.GetOrdinal("CandidateRowsInserted")),
                    ValidationFailureCount = reader.GetInt32(reader.GetOrdinal("ValidationFailureCount"))
                };
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve original failure.
            }

            throw;
        }
    }

    public async Task CompleteRunAsync(
        long executionRunId,
        FacultyPublicationCohortExecutionResult result,
        FacultyPublicationCohortExecutionSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCohortExecutionRun
SET
    EnabledFacultyCount = @EnabledFacultyCount,
    AcceptedAuthorDecisionCount = @AcceptedAuthorDecisionCount,
    RawSourceRowCount = @RawSourceRowCount,
    SelectedCandidateCount = @SelectedCandidateCount,
    DuplicateRawRowsRemoved = @DuplicateRawRowsRemoved,
    ExistingCandidateCount = @ExistingCandidateCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    ValidationFailureCount = @ValidationFailureCount,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    RunStatus = 'Completed',
    SummaryText = @SummaryText,
    ErrorText = NULL,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCohortExecutionRunId = @ExecutionRunId;";

        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@EnabledFacultyCount", result.EnabledFacultyCount);
        command.Parameters.AddWithValue("@AcceptedAuthorDecisionCount", result.AcceptedAuthorDecisionCount);
        command.Parameters.AddWithValue("@RawSourceRowCount", result.RawSourceRowCount);
        command.Parameters.AddWithValue("@SelectedCandidateCount", result.SelectedCandidateCount);
        command.Parameters.AddWithValue("@DuplicateRawRowsRemoved", result.DuplicateRawRowsRemoved);
        command.Parameters.AddWithValue("@ExistingCandidateCount", result.ExistingCandidateCount);
        command.Parameters.AddWithValue("@CandidateRowsInserted", result.CandidateRowsInserted);
        command.Parameters.AddWithValue("@ValidationFailureCount", result.ValidationFailureCount);
        command.Parameters.AddWithValue("@PublicationCandidateRowsAfter", safetyAfter.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsAfter", safetyAfter.PublicationDecisionRows);
        command.Parameters.AddWithValue("@SummaryText", summary);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRunAsync(
        long executionRunId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCohortExecutionRun
SET
    RunStatus = 'Failed',
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCohortExecutionRunId = @ExecutionRunId;";

        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAuditTablesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'openalex.FacultyPublicationCohortExecutionRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.FacultyPublicationCohortExecutionItem', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException(
                "PATCH017B11 audit tables do not exist. Run sql/402_V3E_PATCH017B11_FiveFacultyPublicationCandidateExecution_SQL2016.sql first.");
        }
    }
}

public sealed class FacultyPublicationCohortExecutionOptionsSnapshot
{
    public string CohortName { get; set; } = string.Empty;
    public int ExpectedFacultyCount { get; set; }
    public int ExpectedAcceptedAuthorDecisionCount { get; set; }
    public int ExpectedRawSourceRowCount { get; set; }
    public int ExpectedDeduplicatedCandidateCount { get; set; }
    public int ExpectedDuplicateRawRowsRemoved { get; set; }
    public int ExpectedExistingCandidateCount { get; set; }
    public int MaxCandidates { get; set; }
}

public sealed class FacultyPublicationCohortExecutionSafetyCounts
{
    public int PublicationCandidateRows { get; set; }
    public int PublicationDecisionRows { get; set; }
}

public sealed class FacultyPublicationCohortExecutionResult
{
    public int EnabledFacultyCount { get; set; }
    public int AcceptedAuthorDecisionCount { get; set; }
    public int RawSourceRowCount { get; set; }
    public int SelectedCandidateCount { get; set; }
    public int DuplicateRawRowsRemoved { get; set; }
    public int ExistingCandidateCount { get; set; }
    public int CandidateRowsInserted { get; set; }
    public int ValidationFailureCount { get; set; }
}
