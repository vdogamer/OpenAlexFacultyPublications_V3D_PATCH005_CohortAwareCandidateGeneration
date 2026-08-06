using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcceptedIdentityBridgeExecutionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcceptedIdentityBridgeExecutionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AcceptedIdentityBridgeExecutionSafetyCounts> GetSafetyCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    AuthorCandidateRows = CASE WHEN OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchCandidate) END,
    AuthorDecisionRows = CASE WHEN OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchDecision) END,
    PublicationCandidateRows = CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END,
    PublicationDecisionRows = CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new AcceptedIdentityBridgeExecutionSafetyCounts
        {
            AuthorCandidateRows = reader.GetInt32(0),
            AuthorDecisionRows = reader.GetInt32(1),
            PublicationCandidateRows = reader.GetInt32(2),
            PublicationDecisionRows = reader.GetInt32(3)
        };
    }

    public async Task<long> StartRunAsync(
        long sourcePreviewRunId,
        long facultyId,
        int maxIdentities,
        AcceptedIdentityBridgeExecutionSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityBridgeExecutionRun
(
    SourceAcceptedIdentityBridgePreviewRunId,
    FacultyId,
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
OUTPUT INSERTED.AcceptedIdentityBridgeExecutionRunId
VALUES
(
    @SourcePreviewRunId,
    @FacultyId,
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
        command.Parameters.AddWithValue("@SourcePreviewRunId", sourcePreviewRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
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
UPDATE openalex.AcceptedIdentityBridgeExecutionRun
SET IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AcceptedIdentityBridgeExecutionResult> ExecuteBridgeAsync(
        long executionRunId,
        long sourcePreviewRunId,
        long facultyId,
        int maxIdentities,
        long ingestionRunId,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
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
    FROM openalex.AcceptedIdentityBridgePreviewRun r
    WHERE r.AcceptedIdentityBridgePreviewRunId = @SourcePreviewRunId
      AND r.FacultyId = @FacultyId
      AND r.RunStatus = 'Completed'
)
BEGIN
    RAISERROR('The selected PATCH017B4 preview run is not completed for the requested faculty.', 16, 1);
END;

DECLARE @SelectedPreview TABLE
(
    AcceptedIdentityBridgePreviewItemId BIGINT NOT NULL,
    FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NOT NULL,
    FacultyIdentityCandidateSummaryId BIGINT NULL,
    CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
    PreviewStatus VARCHAR(100) NOT NULL,
    LocalAuthorIdFromPreview BIGINT NULL
);

INSERT INTO @SelectedPreview
(
    AcceptedIdentityBridgePreviewItemId,
    FacultyIdentityCandidateDecisionId,
    FacultyId,
    FacultyDisplayName,
    FacultyIdentityCandidateSummaryId,
    CandidateAuthorOpenAlexId,
    PreviewStatus,
    LocalAuthorIdFromPreview
)
SELECT TOP (@MaxIdentities)
    i.AcceptedIdentityBridgePreviewItemId,
    i.FacultyIdentityCandidateDecisionId,
    i.FacultyId,
    i.FacultyDisplayName,
    i.FacultyIdentityCandidateSummaryId,
    i.CandidateAuthorOpenAlexId,
    i.PreviewStatus,
    i.LocalAuthorId
FROM openalex.AcceptedIdentityBridgePreviewItem i
WHERE i.AcceptedIdentityBridgePreviewRunId = @SourcePreviewRunId
  AND i.FacultyId = @FacultyId
ORDER BY i.AcceptedIdentityBridgePreviewItemId;

IF NOT EXISTS (SELECT 1 FROM @SelectedPreview)
BEGIN
    RAISERROR('The selected PATCH017B4 preview run contains no identities for execution.', 16, 1);
END;

IF EXISTS
(
    SELECT 1
    FROM @SelectedPreview
    WHERE PreviewStatus NOT IN
    (
        'ReadyToCreateCandidateAndDecision',
        'ReadyToRefreshCandidateAndCreateDecision',
        'ReadyToCreateBridgeCandidateOnly',
        'ReadyToBridgeWithMissingLocalAuthorWarning',
        'AlreadyBridged'
    )
)
BEGIN
    RAISERROR('One or more selected preview identities are blocked. Run a new PATCH017B4 preview after resolving the warnings.', 16, 1);
END;

DECLARE @Source TABLE
(
    AcceptedIdentityBridgePreviewItemId BIGINT NOT NULL,
    FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
    FacultyIdentityCandidateSummaryId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NULL,
    CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
    CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
    CandidateDisplayName NVARCHAR(512) NULL,
    CandidateOrcid NVARCHAR(256) NULL,
    BestIdentityEvidenceScore DECIMAL(18,4) NOT NULL,
    IdentityEvidenceJson NVARCHAR(MAX) NULL,
    LocalAuthorId BIGINT NULL,
    ExistingBridgeCandidateIdBefore BIGINT NULL,
    ExistingAcceptedAuthorDecisionIdBefore BIGINT NULL
);

INSERT INTO @Source
(
    AcceptedIdentityBridgePreviewItemId,
    FacultyIdentityCandidateDecisionId,
    FacultyIdentityCandidateSummaryId,
    FacultyId,
    FacultyDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    BestIdentityEvidenceScore,
    IdentityEvidenceJson,
    LocalAuthorId,
    ExistingBridgeCandidateIdBefore,
    ExistingAcceptedAuthorDecisionIdBefore
)
SELECT
    p.AcceptedIdentityBridgePreviewItemId,
    d.FacultyIdentityCandidateDecisionId,
    s.FacultyIdentityCandidateSummaryId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    ISNULL(s.BestIdentityEvidenceScore, 0),
    s.EvidenceJson,
    a.AuthorId,
    c.FacultyAuthorMatchCandidateId,
    ad.FacultyAuthorMatchDecisionId
FROM @SelectedPreview p
JOIN openalex.FacultyIdentityCandidateDecision d
    ON d.FacultyIdentityCandidateDecisionId = p.FacultyIdentityCandidateDecisionId
   AND d.FacultyId = p.FacultyId
   AND d.CandidateAuthorOpenAlexId = p.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1
   AND d.DecisionStatus = 'AcceptedIdentity'
JOIN openalex.FacultyIdentityCandidateSummary s
    ON s.FacultyIdentityCandidateSummaryId = p.FacultyIdentityCandidateSummaryId
   AND s.FacultyId = d.FacultyId
   AND s.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
OUTER APPLY
(
    SELECT TOP (1) a2.AuthorId
    FROM openalex.Author a2
    WHERE
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) =
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(s.CandidateAuthorOpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
        OR
        (
            s.CandidateAuthorOpenAlexShortId IS NOT NULL
            AND a2.OpenAlexShortId = s.CandidateAuthorOpenAlexShortId
        )
        OR
        (
            s.CandidateOrcid IS NOT NULL
            AND a2.Orcid IS NOT NULL
            AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.Orcid)), 'https://orcid.org/', ''), 'http://orcid.org/', '')) =
                UPPER(REPLACE(REPLACE(LTRIM(RTRIM(s.CandidateOrcid)), 'https://orcid.org/', ''), 'http://orcid.org/', ''))
        )
    ORDER BY a2.AuthorId
) a
OUTER APPLY
(
    SELECT TOP (1) c2.FacultyAuthorMatchCandidateId
    FROM openalex.FacultyAuthorMatchCandidate c2
    WHERE c2.FacultyId = s.FacultyId
      AND c2.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
      AND c2.CandidateSource = 'AcceptedIdentityDecision'
    ORDER BY c2.FacultyAuthorMatchCandidateId DESC
) c
OUTER APPLY
(
    SELECT TOP (1) d2.FacultyAuthorMatchDecisionId
    FROM openalex.FacultyAuthorMatchDecision d2
    WHERE d2.FacultyId = s.FacultyId
      AND d2.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
      AND d2.IsCurrent = 1
      AND d2.DecisionStatus = 'Accepted'
    ORDER BY d2.FacultyAuthorMatchDecisionId DESC
) ad;

IF (SELECT COUNT(*) FROM @Source) <> (SELECT COUNT(*) FROM @SelectedPreview)
BEGIN
    RAISERROR('Accepted identity decisions changed after the PATCH017B4 preview. Run a new preview before execution.', 16, 1);
END;

UPDATE c
SET
    c.AuthorId = s.LocalAuthorId,
    c.AuthorOpenAlexShortId = s.CandidateAuthorOpenAlexShortId,
    c.AuthorDisplayName = s.CandidateDisplayName,
    c.AuthorOrcid = s.CandidateOrcid,
    c.CandidateStatus = 'AutoAcceptedCandidate',
    c.MatchScore = s.BestIdentityEvidenceScore,
    c.NameScore = s.BestIdentityEvidenceScore,
    c.OrcidScore = CASE WHEN s.CandidateOrcid IS NULL THEN 0 ELSE 100 END,
    c.InstitutionScore = 0,
    c.CoauthorScore = 0,
    c.PublicationScore = 0,
    c.EvidenceSummary = 'AcceptedIdentityDecision: controlled PATCH017B5 bridge accepted OpenAlex author ' +
        ISNULL(s.CandidateAuthorOpenAlexShortId, s.CandidateAuthorOpenAlexId) + ' for faculty ' +
        ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)) + '. IdentityScore=' +
        CONVERT(NVARCHAR(50), s.BestIdentityEvidenceScore) + '.',
    c.EvidenceJson = s.IdentityEvidenceJson,
    c.IngestionRunId = @IngestionRunId,
    c.UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyAuthorMatchCandidate c
JOIN @Source s
    ON c.FacultyId = s.FacultyId
   AND c.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
   AND c.CandidateSource = 'AcceptedIdentityDecision';

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
    s.BestIdentityEvidenceScore,
    s.BestIdentityEvidenceScore,
    CASE WHEN s.CandidateOrcid IS NULL THEN 0 ELSE 100 END,
    0,
    0,
    0,
    'AcceptedIdentityDecision: controlled PATCH017B5 bridge accepted OpenAlex author ' +
        ISNULL(s.CandidateAuthorOpenAlexShortId, s.CandidateAuthorOpenAlexId) + ' for faculty ' +
        ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)) + '. IdentityScore=' +
        CONVERT(NVARCHAR(50), s.BestIdentityEvidenceScore) + '.',
    s.IdentityEvidenceJson,
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

DECLARE @CandidateMap TABLE
(
    AcceptedIdentityBridgePreviewItemId BIGINT NOT NULL,
    FacultyAuthorMatchCandidateId BIGINT NOT NULL
);

INSERT INTO @CandidateMap
SELECT
    s.AcceptedIdentityBridgePreviewItemId,
    c.FacultyAuthorMatchCandidateId
FROM @Source s
CROSS APPLY
(
    SELECT TOP (1) c2.FacultyAuthorMatchCandidateId
    FROM openalex.FacultyAuthorMatchCandidate c2
    WHERE c2.FacultyId = s.FacultyId
      AND c2.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
      AND c2.CandidateSource = 'AcceptedIdentityDecision'
    ORDER BY c2.FacultyAuthorMatchCandidateId DESC
) c;

UPDATE d
SET
    d.IsCurrent = 0,
    d.UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyAuthorMatchDecision d
JOIN @Source s
    ON d.FacultyId = s.FacultyId
   AND d.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
WHERE d.IsCurrent = 1
  AND s.ExistingAcceptedAuthorDecisionIdBefore IS NULL;

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
    cm.FacultyAuthorMatchCandidateId,
    s.FacultyId,
    s.LocalAuthorId,
    s.CandidateAuthorOpenAlexId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    'Accepted',
    'AcceptedIdentityDecision',
    'IdentityReviewBridge',
    1,
    @ReviewedBy,
    SYSUTCDATETIME(),
    'Generated by controlled PATCH017B5 execution from the selected current AcceptedIdentity decision.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Source s
JOIN @CandidateMap cm
    ON cm.AcceptedIdentityBridgePreviewItemId = s.AcceptedIdentityBridgePreviewItemId
WHERE s.ExistingAcceptedAuthorDecisionIdBefore IS NULL;

DECLARE @DecisionMap TABLE
(
    AcceptedIdentityBridgePreviewItemId BIGINT NOT NULL,
    FacultyAuthorMatchDecisionId BIGINT NOT NULL
);

INSERT INTO @DecisionMap
SELECT
    s.AcceptedIdentityBridgePreviewItemId,
    d.FacultyAuthorMatchDecisionId
FROM @Source s
CROSS APPLY
(
    SELECT TOP (1) d2.FacultyAuthorMatchDecisionId
    FROM openalex.FacultyAuthorMatchDecision d2
    WHERE d2.FacultyId = s.FacultyId
      AND d2.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
      AND d2.IsCurrent = 1
      AND d2.DecisionStatus = 'Accepted'
    ORDER BY d2.FacultyAuthorMatchDecisionId DESC
) d;

INSERT INTO openalex.AcceptedIdentityBridgeExecutionItem
(
    AcceptedIdentityBridgeExecutionRunId,
    SourceAcceptedIdentityBridgePreviewItemId,
    FacultyIdentityCandidateDecisionId,
    FacultyIdentityCandidateSummaryId,
    FacultyId,
    FacultyDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    LocalAuthorId,
    BridgeCandidateIdBefore,
    BridgeCandidateIdAfter,
    AcceptedAuthorDecisionIdBefore,
    AcceptedAuthorDecisionIdAfter,
    CandidateExecutionAction,
    DecisionExecutionAction,
    ExecutionStatus,
    WarningText,
    CreatedAtUtc
)
SELECT
    @ExecutionRunId,
    s.AcceptedIdentityBridgePreviewItemId,
    s.FacultyIdentityCandidateDecisionId,
    s.FacultyIdentityCandidateSummaryId,
    s.FacultyId,
    ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)),
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.LocalAuthorId,
    s.ExistingBridgeCandidateIdBefore,
    cm.FacultyAuthorMatchCandidateId,
    s.ExistingAcceptedAuthorDecisionIdBefore,
    dm.FacultyAuthorMatchDecisionId,
    CASE WHEN s.ExistingBridgeCandidateIdBefore IS NULL THEN 'InsertedAcceptedIdentityCandidate' ELSE 'UpdatedAcceptedIdentityCandidate' END,
    CASE WHEN s.ExistingAcceptedAuthorDecisionIdBefore IS NULL THEN 'InsertedAcceptedAuthorDecision' ELSE 'SkippedAlreadyAcceptedDecision' END,
    'Completed',
    CASE WHEN s.LocalAuthorId IS NULL THEN 'Local openalex.Author row was not found; the accepted author decision retains the OpenAlex author ID and may use a NULL AuthorId.' ELSE NULL END,
    SYSUTCDATETIME()
FROM @Source s
JOIN @CandidateMap cm
    ON cm.AcceptedIdentityBridgePreviewItemId = s.AcceptedIdentityBridgePreviewItemId
JOIN @DecisionMap dm
    ON dm.AcceptedIdentityBridgePreviewItemId = s.AcceptedIdentityBridgePreviewItemId;

SELECT
    SelectedIdentityCount = COUNT(*),
    CandidateRowsInserted = SUM(CASE WHEN ExistingBridgeCandidateIdBefore IS NULL THEN 1 ELSE 0 END),
    CandidateRowsUpdated = SUM(CASE WHEN ExistingBridgeCandidateIdBefore IS NOT NULL THEN 1 ELSE 0 END),
    DecisionRowsInserted = SUM(CASE WHEN ExistingAcceptedAuthorDecisionIdBefore IS NULL THEN 1 ELSE 0 END),
    DecisionsAlreadyAccepted = SUM(CASE WHEN ExistingAcceptedAuthorDecisionIdBefore IS NOT NULL THEN 1 ELSE 0 END),
    MissingLocalAuthorCount = SUM(CASE WHEN LocalAuthorId IS NULL THEN 1 ELSE 0 END)
FROM @Source;";

            command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
            command.Parameters.AddWithValue("@SourcePreviewRunId", sourcePreviewRunId);
            command.Parameters.AddWithValue("@FacultyId", facultyId);
            command.Parameters.AddWithValue("@MaxIdentities", Math.Clamp(maxIdentities, 1, 5));
            command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
            command.Parameters.AddWithValue("@ReviewedBy", string.IsNullOrWhiteSpace(reviewedBy)
                ? "OpenAlex.V3E.PATCH017B5"
                : reviewedBy.Trim());

            AcceptedIdentityBridgeExecutionResult result;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("Controlled accepted-identity bridge did not return a result row.");
                }

                result = new AcceptedIdentityBridgeExecutionResult
                {
                    SelectedIdentityCount = reader.GetInt32(reader.GetOrdinal("SelectedIdentityCount")),
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
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CompleteRunAsync(
        long executionRunId,
        AcceptedIdentityBridgeExecutionResult result,
        AcceptedIdentityBridgeExecutionSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(executionRunId, "Completed", result, safetyAfter, summary, null, cancellationToken);
    }

    public async Task FailRunAsync(
        long executionRunId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(executionRunId, "Failed", null, null, null, exception.ToString(), cancellationToken);
    }

    private async Task UpdateRunAsync(
        long executionRunId,
        string status,
        AcceptedIdentityBridgeExecutionResult? result,
        AcceptedIdentityBridgeExecutionSafetyCounts? safetyAfter,
        string? summary,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityBridgeExecutionRun
SET
    SelectedIdentityCount = @SelectedIdentityCount,
    CandidateRowsInserted = @CandidateRowsInserted,
    CandidateRowsUpdated = @CandidateRowsUpdated,
    DecisionRowsInserted = @DecisionRowsInserted,
    DecisionsAlreadyAccepted = @DecisionsAlreadyAccepted,
    MissingLocalAuthorCount = @MissingLocalAuthorCount,
    AuthorCandidateRowsAfter = @AuthorCandidateRowsAfter,
    AuthorDecisionRowsAfter = @AuthorDecisionRowsAfter,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    RunStatus = @RunStatus,
    SummaryText = @SummaryText,
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityBridgeExecutionRunId = @ExecutionRunId;";
        command.Parameters.AddWithValue("@ExecutionRunId", executionRunId);
        command.Parameters.AddWithValue("@SelectedIdentityCount", result?.SelectedIdentityCount ?? 0);
        command.Parameters.AddWithValue("@CandidateRowsInserted", result?.CandidateRowsInserted ?? 0);
        command.Parameters.AddWithValue("@CandidateRowsUpdated", result?.CandidateRowsUpdated ?? 0);
        command.Parameters.AddWithValue("@DecisionRowsInserted", result?.DecisionRowsInserted ?? 0);
        command.Parameters.AddWithValue("@DecisionsAlreadyAccepted", result?.DecisionsAlreadyAccepted ?? 0);
        command.Parameters.AddWithValue("@MissingLocalAuthorCount", result?.MissingLocalAuthorCount ?? 0);
        command.Parameters.AddWithValue("@AuthorCandidateRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.AuthorCandidateRows);
        command.Parameters.AddWithValue("@AuthorDecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.AuthorDecisionRows);
        command.Parameters.AddWithValue("@PublicationCandidateRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.PublicationDecisionRows);
        command.Parameters.AddWithValue("@RunStatus", status);
        command.Parameters.AddWithValue("@SummaryText", (object?)summary ?? DBNull.Value);
        command.Parameters.AddWithValue("@ErrorText", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.AcceptedIdentityBridgeExecutionItem', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException("PATCH017B5 execution tables do not exist. Run sql/380_V3E_PATCH017B5_AcceptedIdentityBridgeExecution_SQL2016.sql first.");
        }
    }
}
