using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyPublicationCandidateExecutionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyPublicationCandidateExecutionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FacultyPublicationCandidateExecutionSafetyCounts> GetSafetyCountsAsync(
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
            throw new InvalidOperationException("Unable to read PATCH017B7 publication safety counts.");
        }

        return new FacultyPublicationCandidateExecutionSafetyCounts
        {
            PublicationCandidateRows = reader.GetInt32(0),
            PublicationDecisionRows = reader.GetInt32(1)
        };
    }

    public async Task<long> StartRunAsync(
        long previewRunId,
        long facultyId,
        int maxCandidates,
        FacultyPublicationCandidateExecutionSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
IF NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyPublicationCandidatePreviewRun
    WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
      AND FacultyId = @FacultyId
      AND RunStatus = 'Completed'
      AND ApplicationVersion = '3E-PATCH017B6-SplitIdentityAwarePublicationPreview'
)
BEGIN
    RAISERROR('The selected PATCH017B6 preview run does not exist, is not completed, belongs to another faculty member, or is not a PATCH017B6 run.', 16, 1);
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyPublicationCandidatePreviewItem
    WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
      AND FacultyId = @FacultyId
      AND PreviewStatus = 'ReadyToGenerate'
)
BEGIN
    RAISERROR('The selected PATCH017B6 preview contains no ReadyToGenerate rows.', 16, 1);
    RETURN;
END;

INSERT INTO openalex.FacultyPublicationCandidateExecutionRun
(
    SourceFacultyPublicationCandidatePreviewRunId,
    FacultyId,
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
OUTPUT INSERTED.FacultyPublicationCandidateExecutionRunId
VALUES
(
    @PreviewRunId,
    @FacultyId,
    @MaxCandidates,
    1,
    @CandidateRowsBefore,
    @DecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@MaxCandidates", maxCandidates);
        command.Parameters.AddWithValue("@CandidateRowsBefore", safetyBefore.PublicationCandidateRows);
        command.Parameters.AddWithValue("@DecisionRowsBefore", safetyBefore.PublicationDecisionRows);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value == DBNull.Value)
        {
            throw new InvalidOperationException("PATCH017B7 execution run was not created.");
        }

        return Convert.ToInt64(value);
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
UPDATE openalex.FacultyPublicationCandidateExecutionRun
SET IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId
  AND RunStatus = 'Running';";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Unable to attach the PATCH017B7 ingestion audit run.");
        }
    }

    public async Task<FacultyPublicationCandidateExecutionResult> ExecuteAsync(
        long executionRunId,
        long previewRunId,
        long facultyId,
        int maxCandidates,
        long ingestionRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

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
    FROM openalex.FacultyPublicationCandidateExecutionRun WITH (UPDLOCK, HOLDLOCK)
    WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId
      AND SourceFacultyPublicationCandidatePreviewRunId = @PreviewRunId
      AND FacultyId = @FacultyId
      AND ConfirmedExecution = 1
      AND RunStatus = 'Running'
)
BEGIN
    RAISERROR('PATCH017B7 execution run is not in the expected Running state.', 16, 1);
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyPublicationCandidatePreviewRun
    WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId
      AND FacultyId = @FacultyId
      AND RunStatus = 'Completed'
      AND ApplicationVersion = '3E-PATCH017B6-SplitIdentityAwarePublicationPreview'
)
BEGIN
    RAISERROR('The frozen PATCH017B6 source preview is no longer valid.', 16, 1);
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

DECLARE @Selected TABLE
(
    SourcePreviewItemId BIGINT NOT NULL PRIMARY KEY,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NOT NULL,
    FacultyAuthorMatchDecisionId BIGINT NOT NULL,
    SelectedAuthorOpenAlexId VARCHAR(64) NOT NULL,
    SelectedAuthorDisplayName NVARCHAR(512) NULL,
    SelectedAuthorOrcid NVARCHAR(256) NULL,
    WorkAuthorshipId BIGINT NOT NULL,
    WorkId BIGINT NOT NULL,
    WorkOpenAlexId VARCHAR(64) NOT NULL,
    WorkOpenAlexShortId VARCHAR(32) NULL,
    WorkDoi NVARCHAR(512) NULL,
    WorkTitle NVARCHAR(MAX) NULL,
    PublicationYear INT NULL,
    PublicationDate DATE NULL,
    WorkType VARCHAR(100) NULL,
    ProposedMatchScore DECIMAL(9,4) NOT NULL,
    EvidenceSummary NVARCHAR(MAX) NULL,
    EvidenceJson NVARCHAR(MAX) NULL,
    CandidateIdBefore BIGINT NULL,
    CandidateIdAfter BIGINT NULL
);

INSERT INTO @Selected
(
    SourcePreviewItemId,
    FacultyId,
    FacultyDisplayName,
    FacultyAuthorMatchDecisionId,
    SelectedAuthorOpenAlexId,
    SelectedAuthorDisplayName,
    SelectedAuthorOrcid,
    WorkAuthorshipId,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkDoi,
    WorkTitle,
    PublicationYear,
    PublicationDate,
    WorkType,
    ProposedMatchScore,
    EvidenceSummary,
    EvidenceJson,
    CandidateIdBefore
)
SELECT TOP (@MaxCandidates)
    p.FacultyPublicationCandidatePreviewItemId,
    p.FacultyId,
    p.FacultyDisplayName,
    p.FacultyAuthorMatchDecisionId,
    p.AuthorOpenAlexId,
    p.AuthorDisplayName,
    p.AuthorOrcid,
    p.WorkAuthorshipId,
    p.WorkId,
    p.WorkOpenAlexId,
    p.WorkOpenAlexShortId,
    p.WorkDoi,
    p.WorkTitle,
    p.PublicationYear,
    p.PublicationDate,
    p.WorkType,
    p.ProposedMatchScore,
    p.EvidenceSummary,
    p.EvidenceJson,
    ExistingCandidateId =
    (
        SELECT TOP (1) pc.FacultyPublicationCandidateId
        FROM openalex.FacultyPublicationCandidate pc WITH (UPDLOCK, HOLDLOCK)
        WHERE pc.FacultyId = p.FacultyId
          AND pc.WorkId = p.WorkId
          AND pc.CandidateSource = 'AcceptedAuthorMatch'
        ORDER BY pc.FacultyPublicationCandidateId
    )
FROM openalex.FacultyPublicationCandidatePreviewItem p
WHERE p.FacultyPublicationCandidatePreviewRunId = @PreviewRunId
  AND p.FacultyId = @FacultyId
  AND p.PreviewStatus = 'ReadyToGenerate'
  AND p.ProposedCandidateSource = 'AcceptedAuthorMatch'
  AND p.ProposedCandidateStatus = 'PendingReview'
  AND p.FacultyAuthorMatchDecisionId IS NOT NULL
  AND p.AuthorOpenAlexId IS NOT NULL
  AND p.WorkAuthorshipId IS NOT NULL
ORDER BY p.FacultyPublicationCandidatePreviewItemId;

DECLARE @SelectedCandidateCount INT = (SELECT COUNT(*) FROM @Selected);
IF @SelectedCandidateCount = 0
BEGIN
    RAISERROR('PATCH017B7 selected no eligible ReadyToGenerate rows.', 16, 1);
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
      AND d.AuthorOpenAlexId = s.SelectedAuthorOpenAlexId
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
          wa.AuthorOpenAlexId = s.SelectedAuthorOpenAlexId
          OR
          (
              s.SelectedAuthorOrcid IS NOT NULL
              AND wa.AuthorOrcid IS NOT NULL
              AND wa.AuthorOrcid = s.SelectedAuthorOrcid
          )
      )
);

IF @ValidationFailureCount > 0
BEGIN
    RAISERROR('PATCH017B7 revalidation failed. At least one frozen B6 row no longer has the same current accepted author/work/authorship evidence. No candidates were written.', 16, 1);
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
    s.SelectedAuthorOpenAlexId,
    s.SelectedAuthorDisplayName,
    s.SelectedAuthorOrcid,
    'AcceptedAuthorMatch',
    'PendingReview',
    s.ProposedMatchScore,
    'PATCH017B7 controlled execution from PATCH017B6 preview run ' + CONVERT(NVARCHAR(30), @PreviewRunId) + '. ' + ISNULL(s.EvidenceSummary, ''),
    s.EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Selected s
WHERE s.CandidateIdBefore IS NULL;

DECLARE @CandidateRowsInserted INT = @@ROWCOUNT;

UPDATE s
SET CandidateIdAfter =
(
    SELECT TOP (1) c.FacultyPublicationCandidateId
    FROM openalex.FacultyPublicationCandidate c
    WHERE c.FacultyId = s.FacultyId
      AND c.WorkId = s.WorkId
      AND c.CandidateSource = 'AcceptedAuthorMatch'
    ORDER BY c.FacultyPublicationCandidateId
)
FROM @Selected s;

IF EXISTS (SELECT 1 FROM @Selected WHERE CandidateIdAfter IS NULL)
BEGIN
    RAISERROR('PATCH017B7 could not verify a publication candidate after execution. The transaction will be rolled back.', 16, 1);
    RETURN;
END;

INSERT INTO openalex.FacultyPublicationCandidateExecutionItem
(
    FacultyPublicationCandidateExecutionRunId,
    SourceFacultyPublicationCandidatePreviewItemId,
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    FacultyAuthorMatchDecisionId,
    SelectedAuthorOpenAlexId,
    WorkAuthorshipId,
    ProposedMatchScore,
    CandidateIdBefore,
    CandidateIdAfter,
    CandidateExecutionAction,
    ExecutionStatus,
    WarningText,
    CreatedAtUtc
)
SELECT
    @ExecutionRunId,
    s.SourcePreviewItemId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.WorkId,
    s.WorkOpenAlexId,
    s.WorkTitle,
    s.FacultyAuthorMatchDecisionId,
    s.SelectedAuthorOpenAlexId,
    s.WorkAuthorshipId,
    s.ProposedMatchScore,
    s.CandidateIdBefore,
    s.CandidateIdAfter,
    CASE WHEN s.CandidateIdBefore IS NULL THEN 'InsertedPendingReview' ELSE 'AlreadyExistsNoChange' END,
    'Completed',
    CASE WHEN s.CandidateIdBefore IS NULL THEN NULL ELSE 'An AcceptedAuthorMatch candidate already existed; PATCH017B7 did not modify it.' END,
    SYSUTCDATETIME()
FROM @Selected s;

DECLARE @CandidatesAlreadyExist INT = @SelectedCandidateCount - @CandidateRowsInserted;
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
    RAISERROR('PATCH017B7 safety verification failed inside the transaction. Candidate or publication-decision counts changed unexpectedly; the transaction will be rolled back.', 16, 1);
    RETURN;
END;

SELECT
    @SelectedCandidateCount AS SelectedCandidateCount,
    @CandidateRowsInserted AS CandidateRowsInserted,
    @CandidatesAlreadyExist AS CandidatesAlreadyExist,
    @ValidationFailureCount AS ValidationFailureCount;";

            command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
            command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
            command.Parameters.AddWithValue("@FacultyId", facultyId);
            command.Parameters.AddWithValue("@MaxCandidates", Math.Clamp(maxCandidates, 1, 5));
            command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);

            FacultyPublicationCandidateExecutionResult result;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("PATCH017B7 execution did not return a result row.");
                }

                result = new FacultyPublicationCandidateExecutionResult
                {
                    SelectedCandidateCount = reader.GetInt32(reader.GetOrdinal("SelectedCandidateCount")),
                    CandidateRowsInserted = reader.GetInt32(reader.GetOrdinal("CandidateRowsInserted")),
                    CandidatesAlreadyExist = reader.GetInt32(reader.GetOrdinal("CandidatesAlreadyExist")),
                    ValidationFailureCount = reader.GetInt32(reader.GetOrdinal("ValidationFailureCount"))
                };
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task CompleteRunAsync(
        long executionRunId,
        FacultyPublicationCandidateExecutionResult result,
        FacultyPublicationCandidateExecutionSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCandidateExecutionRun
SET SelectedCandidateCount = @SelectedCandidateCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    CandidatesAlreadyExist = @CandidatesAlreadyExist,
    ValidationFailureCount = @ValidationFailureCount,
    PublicationCandidateRowsAfter = @CandidateRowsAfter,
    PublicationDecisionRowsAfter = @DecisionRowsAfter,
    RunStatus = 'Completed',
    SummaryText = @SummaryText,
    ErrorText = NULL,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@SelectedCandidateCount", result.SelectedCandidateCount);
        command.Parameters.AddWithValue("@CandidateRowsInserted", result.CandidateRowsInserted);
        command.Parameters.AddWithValue("@CandidatesAlreadyExist", result.CandidatesAlreadyExist);
        command.Parameters.AddWithValue("@ValidationFailureCount", result.ValidationFailureCount);
        command.Parameters.AddWithValue("@CandidateRowsAfter", safetyAfter.PublicationCandidateRows);
        command.Parameters.AddWithValue("@DecisionRowsAfter", safetyAfter.PublicationDecisionRows);
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
UPDATE openalex.FacultyPublicationCandidateExecutionRun
SET RunStatus = 'Failed',
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidateExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.FacultyPublicationCandidateExecutionItem', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException(
                "PATCH017B7 execution tables do not exist. Run sql/383_V3E_PATCH017B7_FacultyPublicationCandidateExecution_SQL2016.sql first.");
        }
    }
}