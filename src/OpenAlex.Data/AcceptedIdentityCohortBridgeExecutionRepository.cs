using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class AcceptedIdentityCohortBridgeExecutionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcceptedIdentityCohortBridgeExecutionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AcceptedIdentityCohortBridgeSafetyCounts> GetSafetyCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    AuthorCandidateRows = (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchCandidate),
    AuthorDecisionRows = (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchDecision),
    PublicationCandidateRows =
        CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
             THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END,
    PublicationDecisionRows =
        CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
             THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read bridge safety counts.");
        }

        return new AcceptedIdentityCohortBridgeSafetyCounts
        {
            AuthorCandidateRows = reader.GetInt32(0),
            AuthorDecisionRows = reader.GetInt32(1),
            PublicationCandidateRows = reader.GetInt32(2),
            PublicationDecisionRows = reader.GetInt32(3)
        };
    }

    public async Task<long> StartAuditRunAsync(
        string cohortName,
        int expectedFacultyCount,
        int maxIdentities,
        AcceptedIdentityCohortBridgeSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureAuditTableAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityCohortBridgeExecutionRun
(
    CohortName,
    ExpectedFacultyCount,
    MaxIdentities,
    ConfirmedBridge,
    AuthorCandidateRowsBefore,
    AuthorDecisionRowsBefore,
    PublicationCandidateRowsBefore,
    PublicationDecisionRowsBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AcceptedIdentityCohortBridgeExecutionRunId
VALUES
(
    @CohortName,
    @ExpectedFacultyCount,
    @MaxIdentities,
    1,
    @AuthorCandidateRowsBefore,
    @AuthorDecisionRowsBefore,
    @PublicationCandidateRowsBefore,
    @PublicationDecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@CohortName", cohortName);
        command.Parameters.AddWithValue("@ExpectedFacultyCount", expectedFacultyCount);
        command.Parameters.AddWithValue("@MaxIdentities", maxIdentities);
        command.Parameters.AddWithValue("@AuthorCandidateRowsBefore", safetyBefore.AuthorCandidateRows);
        command.Parameters.AddWithValue("@AuthorDecisionRowsBefore", safetyBefore.AuthorDecisionRows);
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
UPDATE openalex.AcceptedIdentityCohortBridgeExecutionRun
SET IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityCohortBridgeExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AcceptedIdentityCohortBridgeExecutionResult> ExecuteBridgeAsync(
        string cohortName,
        int expectedFacultyCount,
        int maxIdentities,
        long ingestionRunId,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT pc.FacultyId)
    FROM openalex.PilotFacultyCohort pc
    WHERE pc.CohortName = @CohortName
      AND pc.IsEnabled = 1
);

IF @EnabledFacultyCount <> @ExpectedFacultyCount
BEGIN
    RAISERROR('Cohort faculty count changed after preflight. Controlled bridge aborted.', 16, 1);
    RETURN;
END;

DECLARE @FacultyWithAcceptedIdentity INT =
(
    SELECT COUNT(DISTINCT d.FacultyId)
    FROM openalex.FacultyIdentityCandidateDecision d
    WHERE d.IsCurrent = 1
      AND d.DecisionStatus = 'AcceptedIdentity'
      AND EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.CohortName = @CohortName
            AND pc.IsEnabled = 1
            AND pc.FacultyId = d.FacultyId
      )
);

IF @FacultyWithAcceptedIdentity <> @EnabledFacultyCount
BEGIN
    RAISERROR('At least one enabled cohort faculty has no current AcceptedIdentity decision. Controlled bridge aborted.', 16, 1);
    RETURN;
END;

DECLARE @AcceptedIdentityCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyIdentityCandidateDecision d
    WHERE d.IsCurrent = 1
      AND d.DecisionStatus = 'AcceptedIdentity'
      AND EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.CohortName = @CohortName
            AND pc.IsEnabled = 1
            AND pc.FacultyId = d.FacultyId
      )
);

IF @AcceptedIdentityCount = 0 OR @AcceptedIdentityCount > @MaxIdentities
BEGIN
    RAISERROR('Accepted identity count is zero or exceeds MaxIdentities. Controlled bridge aborted.', 16, 1);
    RETURN;
END;

DECLARE @Source TABLE
(
    FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
    FacultyIdentityCandidateSummaryId BIGINT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NULL,
    CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
    CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
    CandidateDisplayName NVARCHAR(512) NULL,
    CandidateOrcid NVARCHAR(256) NULL,
    BestIdentityEvidenceScore DECIMAL(18,4) NULL,
    EvidenceJson NVARCHAR(MAX) NULL,
    LocalAuthorId BIGINT NULL
);

INSERT INTO @Source
(
    FacultyIdentityCandidateDecisionId,
    FacultyIdentityCandidateSummaryId,
    FacultyId,
    FacultyDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    BestIdentityEvidenceScore,
    EvidenceJson,
    LocalAuthorId
)
SELECT
    d.FacultyIdentityCandidateDecisionId,
    s.FacultyIdentityCandidateSummaryId,
    d.FacultyId,
    f.DisplayName,
    d.CandidateAuthorOpenAlexId,
    COALESCE(s.CandidateAuthorOpenAlexShortId, d.CandidateAuthorOpenAlexShortId),
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.BestIdentityEvidenceScore,
    s.EvidenceJson,
    a.AuthorId
FROM openalex.FacultyIdentityCandidateDecision d
INNER JOIN openalex.Faculty f
    ON f.FacultyId = d.FacultyId
OUTER APPLY
(
    SELECT TOP (1)
        s2.FacultyIdentityCandidateSummaryId,
        s2.CandidateAuthorOpenAlexShortId,
        s2.CandidateDisplayName,
        s2.CandidateOrcid,
        s2.BestIdentityEvidenceScore,
        s2.EvidenceJson
    FROM openalex.FacultyIdentityCandidateSummary s2
    WHERE s2.FacultyId = d.FacultyId
      AND s2.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
    ORDER BY s2.BestIdentityEvidenceScore DESC,
             s2.FacultyIdentityCandidateSummaryId DESC
) s
OUTER APPLY
(
    SELECT TOP (1)
        a2.AuthorId
    FROM openalex.Author a2
    WHERE
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.OpenAlexId)),
            'https://openalex.org/', ''), 'http://openalex.org/', '')) =
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(d.CandidateAuthorOpenAlexId)),
            'https://openalex.org/', ''), 'http://openalex.org/', ''))
        OR
        (
            s.CandidateAuthorOpenAlexShortId IS NOT NULL
            AND a2.OpenAlexShortId = s.CandidateAuthorOpenAlexShortId
        )
        OR
        (
            s.CandidateOrcid IS NOT NULL
            AND a2.Orcid IS NOT NULL
            AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.Orcid)),
                'https://orcid.org/', ''), 'http://orcid.org/', '')) =
                UPPER(REPLACE(REPLACE(LTRIM(RTRIM(s.CandidateOrcid)),
                'https://orcid.org/', ''), 'http://orcid.org/', ''))
        )
    ORDER BY a2.AuthorId
) a
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'AcceptedIdentity'
  AND EXISTS
  (
      SELECT 1
      FROM openalex.PilotFacultyCohort pc
      WHERE pc.CohortName = @CohortName
        AND pc.IsEnabled = 1
        AND pc.FacultyId = d.FacultyId
  );

IF (SELECT COUNT(*) FROM @Source) <> @AcceptedIdentityCount
BEGIN
    RAISERROR('Accepted identity source count changed during controlled bridge. Transaction rolled back.', 16, 1);
    RETURN;
END;

IF EXISTS
(
    SELECT 1 FROM @Source
    WHERE FacultyIdentityCandidateSummaryId IS NULL
)
BEGIN
    RAISERROR('One or more accepted identities no longer has an identity summary. Transaction rolled back.', 16, 1);
    RETURN;
END;

DECLARE @MissingLocalAuthorCount INT =
(
    SELECT COUNT(*) FROM @Source WHERE LocalAuthorId IS NULL
);

UPDATE c
SET
    c.AuthorId = s.LocalAuthorId,
    c.AuthorOpenAlexShortId = s.CandidateAuthorOpenAlexShortId,
    c.AuthorDisplayName = s.CandidateDisplayName,
    c.AuthorOrcid = s.CandidateOrcid,
    c.CandidateStatus = 'AutoAcceptedCandidate',
    c.MatchScore = ISNULL(s.BestIdentityEvidenceScore, 0),
    c.NameScore = ISNULL(s.BestIdentityEvidenceScore, 0),
    c.OrcidScore = CASE WHEN s.CandidateOrcid IS NULL THEN 0 ELSE 100 END,
    c.InstitutionScore = 0,
    c.CoauthorScore = 0,
    c.PublicationScore = 0,
    c.EvidenceSummary =
        'AcceptedIdentityDecision: cohort identity review accepted OpenAlex author ' +
        ISNULL(s.CandidateAuthorOpenAlexShortId, s.CandidateAuthorOpenAlexId) +
        ' for faculty ' + ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)) +
        '. IdentityScore=' + CONVERT(NVARCHAR(50), ISNULL(s.BestIdentityEvidenceScore, 0)) + '.',
    c.EvidenceJson = s.EvidenceJson,
    c.IngestionRunId = @IngestionRunId,
    c.UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyAuthorMatchCandidate c
INNER JOIN @Source s
    ON s.FacultyId = c.FacultyId
   AND s.CandidateAuthorOpenAlexId = c.AuthorOpenAlexId
WHERE c.CandidateSource = 'AcceptedIdentityDecision';

DECLARE @CandidateRowsUpdated INT = @@ROWCOUNT;

INSERT INTO openalex.FacultyAuthorMatchCandidate
(
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorOpenAlexShortId,
    AuthorDisplayName,
    AuthorOrcid,
    CandidateSource,
    CandidateStatus,
    MatchScore,
    NameScore,
    OrcidScore,
    InstitutionScore,
    CoauthorScore,
    PublicationScore,
    EvidenceSummary,
    EvidenceJson,
    IngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    s.FacultyId,
    s.LocalAuthorId,
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    'AcceptedIdentityDecision',
    'AutoAcceptedCandidate',
    ISNULL(s.BestIdentityEvidenceScore, 0),
    ISNULL(s.BestIdentityEvidenceScore, 0),
    CASE WHEN s.CandidateOrcid IS NULL THEN 0 ELSE 100 END,
    0,
    0,
    0,
    'AcceptedIdentityDecision: cohort identity review accepted OpenAlex author ' +
        ISNULL(s.CandidateAuthorOpenAlexShortId, s.CandidateAuthorOpenAlexId) +
        ' for faculty ' + ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)) +
        '. IdentityScore=' + CONVERT(NVARCHAR(50), ISNULL(s.BestIdentityEvidenceScore, 0)) + '.',
    s.EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Source s
WHERE NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyAuthorMatchCandidate c
    WHERE c.FacultyId = s.FacultyId
      AND c.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
      AND c.CandidateSource = 'AcceptedIdentityDecision'
);

DECLARE @CandidateRowsInserted INT = @@ROWCOUNT;

DECLARE @AlreadyAcceptedCount INT =
(
    SELECT COUNT(*)
    FROM @Source s
    WHERE EXISTS
    (
        SELECT 1
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = s.FacultyId
          AND d.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
          AND d.IsCurrent = 1
          AND d.DecisionStatus = 'Accepted'
    )
);

DECLARE @DecisionSources TABLE
(
    FacultyAuthorMatchCandidateId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    AuthorId BIGINT NULL,
    AuthorOpenAlexId VARCHAR(128) NOT NULL,
    AuthorDisplayName NVARCHAR(512) NULL,
    AuthorOrcid NVARCHAR(256) NULL
);

INSERT INTO @DecisionSources
(
    FacultyAuthorMatchCandidateId,
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid
)
SELECT
    c.FacultyAuthorMatchCandidateId,
    c.FacultyId,
    c.AuthorId,
    c.AuthorOpenAlexId,
    c.AuthorDisplayName,
    c.AuthorOrcid
FROM openalex.FacultyAuthorMatchCandidate c
INNER JOIN @Source s
    ON s.FacultyId = c.FacultyId
   AND s.CandidateAuthorOpenAlexId = c.AuthorOpenAlexId
WHERE c.CandidateSource = 'AcceptedIdentityDecision'
  AND NOT EXISTS
  (
      SELECT 1
      FROM openalex.FacultyAuthorMatchDecision d
      WHERE d.FacultyId = c.FacultyId
        AND d.AuthorOpenAlexId = c.AuthorOpenAlexId
        AND d.IsCurrent = 1
        AND d.DecisionStatus = 'Accepted'
  );

UPDATE d
SET
    d.IsCurrent = 0,
    d.UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyAuthorMatchDecision d
INNER JOIN @DecisionSources s
    ON s.FacultyId = d.FacultyId
   AND s.AuthorOpenAlexId = d.AuthorOpenAlexId
WHERE d.IsCurrent = 1;

INSERT INTO openalex.FacultyAuthorMatchDecision
(
    FacultyAuthorMatchCandidateId,
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    DecisionStatus,
    DecisionReason,
    DecisionSource,
    IsCurrent,
    ReviewedBy,
    ReviewedAtUtc,
    ReviewerNotes,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    s.FacultyAuthorMatchCandidateId,
    s.FacultyId,
    s.AuthorId,
    s.AuthorOpenAlexId,
    s.AuthorDisplayName,
    s.AuthorOrcid,
    'Accepted',
    'AcceptedIdentityDecision',
    'IdentityReviewBridge',
    1,
    @ReviewedBy,
    SYSUTCDATETIME(),
    'PATCH017B9 controlled cohort bridge from current AcceptedIdentity decision.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @DecisionSources s;

DECLARE @DecisionRowsInserted INT = @@ROWCOUNT;

SELECT
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithAcceptedIdentity = @FacultyWithAcceptedIdentity,
    SelectedAcceptedIdentityCount = @AcceptedIdentityCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    CandidateRowsUpdated = @CandidateRowsUpdated,
    DecisionRowsInserted = @DecisionRowsInserted,
    DecisionsAlreadyAccepted = @AlreadyAcceptedCount,
    MissingLocalAuthorCount = @MissingLocalAuthorCount;";

            command.Parameters.AddWithValue("@CohortName", cohortName);
            command.Parameters.AddWithValue("@ExpectedFacultyCount", expectedFacultyCount);
            command.Parameters.AddWithValue("@MaxIdentities", maxIdentities);
            command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
            command.Parameters.AddWithValue("@ReviewedBy", reviewedBy);

            AcceptedIdentityCohortBridgeExecutionResult result;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("Controlled cohort bridge returned no result row.");
                }

                result = new AcceptedIdentityCohortBridgeExecutionResult
                {
                    EnabledFacultyCount = reader.GetInt32(reader.GetOrdinal("EnabledFacultyCount")),
                    FacultyWithAcceptedIdentity = reader.GetInt32(reader.GetOrdinal("FacultyWithAcceptedIdentity")),
                    SelectedAcceptedIdentityCount = reader.GetInt32(reader.GetOrdinal("SelectedAcceptedIdentityCount")),
                    CandidateRowsInserted = reader.GetInt32(reader.GetOrdinal("CandidateRowsInserted")),
                    CandidateRowsUpdated = reader.GetInt32(reader.GetOrdinal("CandidateRowsUpdated")),
                    DecisionRowsInserted = reader.GetInt32(reader.GetOrdinal("DecisionRowsInserted")),
                    DecisionsAlreadyAccepted = reader.GetInt32(reader.GetOrdinal("DecisionsAlreadyAccepted")),
                    MissingLocalAuthorCount = reader.GetInt32(reader.GetOrdinal("MissingLocalAuthorCount"))
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
                // Preserve original exception.
            }

            throw;
        }
    }

    public async Task CompleteAuditRunAsync(
        long executionRunId,
        AcceptedIdentityCohortBridgeExecutionResult result,
        AcceptedIdentityCohortBridgeSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityCohortBridgeExecutionRun
SET
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithAcceptedIdentity = @FacultyWithAcceptedIdentity,
    SelectedAcceptedIdentityCount = @SelectedAcceptedIdentityCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    CandidateRowsUpdated = @CandidateRowsUpdated,
    DecisionRowsInserted = @DecisionRowsInserted,
    DecisionsAlreadyAccepted = @DecisionsAlreadyAccepted,
    MissingLocalAuthorCount = @MissingLocalAuthorCount,
    AuthorCandidateRowsAfter = @AuthorCandidateRowsAfter,
    AuthorDecisionRowsAfter = @AuthorDecisionRowsAfter,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    RunStatus = 'Completed',
    SummaryText = @SummaryText,
    ErrorText = NULL,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityCohortBridgeExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@EnabledFacultyCount", result.EnabledFacultyCount);
        command.Parameters.AddWithValue("@FacultyWithAcceptedIdentity", result.FacultyWithAcceptedIdentity);
        command.Parameters.AddWithValue("@SelectedAcceptedIdentityCount", result.SelectedAcceptedIdentityCount);
        command.Parameters.AddWithValue("@CandidateRowsInserted", result.CandidateRowsInserted);
        command.Parameters.AddWithValue("@CandidateRowsUpdated", result.CandidateRowsUpdated);
        command.Parameters.AddWithValue("@DecisionRowsInserted", result.DecisionRowsInserted);
        command.Parameters.AddWithValue("@DecisionsAlreadyAccepted", result.DecisionsAlreadyAccepted);
        command.Parameters.AddWithValue("@MissingLocalAuthorCount", result.MissingLocalAuthorCount);
        command.Parameters.AddWithValue("@AuthorCandidateRowsAfter", safetyAfter.AuthorCandidateRows);
        command.Parameters.AddWithValue("@AuthorDecisionRowsAfter", safetyAfter.AuthorDecisionRows);
        command.Parameters.AddWithValue("@PublicationCandidateRowsAfter", safetyAfter.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsAfter", safetyAfter.PublicationDecisionRows);
        command.Parameters.AddWithValue("@SummaryText", summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailAuditRunAsync(
        long executionRunId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityCohortBridgeExecutionRun
SET
    RunStatus = 'Failed',
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityCohortBridgeExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAuditTableAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE WHEN OBJECT_ID(
    N'openalex.AcceptedIdentityCohortBridgeExecutionRun', N'U') IS NULL
    THEN 0 ELSE 1 END;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException(
                "openalex.AcceptedIdentityCohortBridgeExecutionRun does not exist. Run sql/396_V3E_PATCH017B9_FiveFacultyAcceptedIdentityBridgeExecution_SQL2016.sql first.");
        }
    }
}

public sealed class AcceptedIdentityCohortBridgeSafetyCounts
{
    public int AuthorCandidateRows { get; set; }
    public int AuthorDecisionRows { get; set; }
    public int PublicationCandidateRows { get; set; }
    public int PublicationDecisionRows { get; set; }
}

public sealed class AcceptedIdentityCohortBridgeExecutionResult
{
    public int EnabledFacultyCount { get; set; }
    public int FacultyWithAcceptedIdentity { get; set; }
    public int SelectedAcceptedIdentityCount { get; set; }
    public int CandidateRowsInserted { get; set; }
    public int CandidateRowsUpdated { get; set; }
    public int DecisionRowsInserted { get; set; }
    public int DecisionsAlreadyAccepted { get; set; }
    public int MissingLocalAuthorCount { get; set; }
}
