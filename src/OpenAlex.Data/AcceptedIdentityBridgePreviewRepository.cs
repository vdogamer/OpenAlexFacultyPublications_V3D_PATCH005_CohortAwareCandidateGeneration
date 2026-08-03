using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcceptedIdentityBridgePreviewRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcceptedIdentityBridgePreviewRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AcceptedIdentityBridgePreviewSafetyCounts> GetSafetyCountsAsync(
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

        return new AcceptedIdentityBridgePreviewSafetyCounts
        {
            AuthorCandidateRows = reader.GetInt32(0),
            AuthorDecisionRows = reader.GetInt32(1),
            PublicationCandidateRows = reader.GetInt32(2),
            PublicationDecisionRows = reader.GetInt32(3)
        };
    }

    public async Task<IReadOnlyList<AcceptedIdentityBridgePreviewSourceRow>> LoadSourceRowsAsync(
        long sourceIdentityResolutionPreviewRunId,
        long facultyId,
        int maxIdentities,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AcceptedIdentityBridgePreviewSourceRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyAuthorIdentityResolutionPreviewRun", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.FacultyAuthorIdentityResolutionPreviewCandidate", "U", cancellationToken))
        {
            throw new InvalidOperationException("PATCH017B3 identity-resolution preview tables do not exist. Run sql/376_V3E_PATCH017B3_FacultyAuthorIdentityResolutionPreview_SQL2016.sql first.");
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateDecision", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateSummary", "U", cancellationToken))
        {
            throw new InvalidOperationException("Identity review decision or summary tables do not exist.");
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchCandidate", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchDecision", "U", cancellationToken))
        {
            throw new InvalidOperationException("Author review candidate or decision tables do not exist.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
;WITH SelectedPreviewCandidates AS
(
    SELECT TOP (@MaxIdentities)
        p.CandidateRank,
        p.FacultyId,
        p.FacultyDisplayName,
        p.CandidateAuthorOpenAlexId,
        p.CandidateAuthorDisplayName,
        p.CandidateAuthorOrcid
    FROM openalex.FacultyAuthorIdentityResolutionPreviewCandidate p
    JOIN openalex.FacultyAuthorIdentityResolutionPreviewRun r
        ON r.FacultyAuthorIdentityResolutionPreviewRunId = p.FacultyAuthorIdentityResolutionPreviewRunId
    WHERE p.FacultyAuthorIdentityResolutionPreviewRunId = @SourcePreviewRunId
      AND p.FacultyId = @FacultyId
      AND r.RunStatus = 'Completed'
    ORDER BY p.CandidateRank, p.ProposedIdentityScore DESC, p.CandidateAuthorOpenAlexId
)
SELECT
    @SourcePreviewRunId AS SourceIdentityResolutionPreviewRunId,
    d.FacultyIdentityCandidateDecisionId,
    d.FacultyId,
    sp.FacultyDisplayName,
    s.FacultyIdentityCandidateSummaryId,
    d.CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId = COALESCE(s.CandidateAuthorOpenAlexShortId, d.CandidateAuthorOpenAlexShortId),
    CandidateDisplayName = COALESCE(s.CandidateDisplayName, sp.CandidateAuthorDisplayName),
    CandidateOrcid = COALESCE(s.CandidateOrcid, sp.CandidateAuthorOrcid),
    s.BestIdentityEvidenceScore,
    IdentitySummaryText = s.SummaryText,
    IdentityEvidenceJson = s.EvidenceJson,
    LocalAuthorId = a.AuthorId,
    ExistingBridgeCandidateId = c.FacultyAuthorMatchCandidateId,
    ExistingBridgeCandidateStatus = c.CandidateStatus,
    ExistingBridgeCandidateMatchScore = c.MatchScore,
    ExistingAcceptedAuthorDecisionId = ad.FacultyAuthorMatchDecisionId,
    CurrentAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d2
        WHERE d2.FacultyId = d.FacultyId
          AND d2.AuthorOpenAlexId = d.CandidateAuthorOpenAlexId
          AND d2.IsCurrent = 1
    )
FROM SelectedPreviewCandidates sp
JOIN openalex.FacultyIdentityCandidateDecision d
    ON d.FacultyId = sp.FacultyId
   AND d.CandidateAuthorOpenAlexId = sp.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1
   AND d.DecisionStatus = 'AcceptedIdentity'
OUTER APPLY
(
    SELECT TOP (1)
        s2.FacultyIdentityCandidateSummaryId,
        s2.CandidateAuthorOpenAlexShortId,
        s2.CandidateDisplayName,
        s2.CandidateOrcid,
        s2.BestIdentityEvidenceScore,
        s2.SummaryText,
        s2.EvidenceJson
    FROM openalex.FacultyIdentityCandidateSummary s2
    WHERE s2.FacultyId = d.FacultyId
      AND s2.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
    ORDER BY s2.BestIdentityEvidenceScore DESC, s2.FacultyIdentityCandidateSummaryId DESC
) s
OUTER APPLY
(
    SELECT TOP (1) a2.AuthorId
    FROM openalex.Author a2
    WHERE
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(a2.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) =
        UPPER(REPLACE(REPLACE(LTRIM(RTRIM(d.CandidateAuthorOpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
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
    SELECT TOP (1)
        c2.FacultyAuthorMatchCandidateId,
        c2.CandidateStatus,
        c2.MatchScore
    FROM openalex.FacultyAuthorMatchCandidate c2
    WHERE c2.FacultyId = d.FacultyId
      AND c2.AuthorOpenAlexId = d.CandidateAuthorOpenAlexId
      AND c2.CandidateSource = 'AcceptedIdentityDecision'
    ORDER BY c2.FacultyAuthorMatchCandidateId DESC
) c
OUTER APPLY
(
    SELECT TOP (1) d3.FacultyAuthorMatchDecisionId
    FROM openalex.FacultyAuthorMatchDecision d3
    WHERE d3.FacultyId = d.FacultyId
      AND d3.AuthorOpenAlexId = d.CandidateAuthorOpenAlexId
      AND d3.IsCurrent = 1
      AND d3.DecisionStatus = 'Accepted'
    ORDER BY d3.FacultyAuthorMatchDecisionId DESC
) ad
ORDER BY sp.CandidateRank, d.CandidateAuthorOpenAlexId;";

        command.Parameters.AddWithValue("@SourcePreviewRunId", sourceIdentityResolutionPreviewRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@MaxIdentities", Math.Clamp(maxIdentities, 1, 5));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcceptedIdentityBridgePreviewSourceRow
            {
                SourceIdentityResolutionPreviewRunId = reader.GetInt64(reader.GetOrdinal("SourceIdentityResolutionPreviewRunId")),
                FacultyIdentityCandidateDecisionId = reader.GetInt64(reader.GetOrdinal("FacultyIdentityCandidateDecisionId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                FacultyIdentityCandidateSummaryId = GetNullableInt64(reader, "FacultyIdentityCandidateSummaryId"),
                CandidateAuthorOpenAlexId = reader.GetString(reader.GetOrdinal("CandidateAuthorOpenAlexId")),
                CandidateAuthorOpenAlexShortId = GetNullableString(reader, "CandidateAuthorOpenAlexShortId"),
                CandidateDisplayName = GetNullableString(reader, "CandidateDisplayName"),
                CandidateOrcid = GetNullableString(reader, "CandidateOrcid"),
                BestIdentityEvidenceScore = GetNullableDecimal(reader, "BestIdentityEvidenceScore"),
                IdentitySummaryText = GetNullableString(reader, "IdentitySummaryText"),
                IdentityEvidenceJson = GetNullableString(reader, "IdentityEvidenceJson"),
                LocalAuthorId = GetNullableInt64(reader, "LocalAuthorId"),
                ExistingBridgeCandidateId = GetNullableInt64(reader, "ExistingBridgeCandidateId"),
                ExistingBridgeCandidateStatus = GetNullableString(reader, "ExistingBridgeCandidateStatus"),
                ExistingBridgeCandidateMatchScore = GetNullableDecimal(reader, "ExistingBridgeCandidateMatchScore"),
                ExistingAcceptedAuthorDecisionId = GetNullableInt64(reader, "ExistingAcceptedAuthorDecisionId"),
                CurrentAuthorDecisionCount = reader.GetInt32(reader.GetOrdinal("CurrentAuthorDecisionCount"))
            });
        }

        return rows;
    }

    public async Task<long> StartRunAsync(
        long sourceIdentityResolutionPreviewRunId,
        long facultyId,
        int maxIdentities,
        AcceptedIdentityBridgePreviewSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityBridgePreviewRun
(
    SourceIdentityResolutionPreviewRunId,
    FacultyId,
    MaxIdentities,
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
OUTPUT INSERTED.AcceptedIdentityBridgePreviewRunId
VALUES
(
    @SourcePreviewRunId,
    @FacultyId,
    @MaxIdentities,
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
        command.Parameters.AddWithValue("@SourcePreviewRunId", sourceIdentityResolutionPreviewRunId);
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

    public async Task SaveItemAsync(
        long previewRunId,
        AcceptedIdentityBridgePreviewSourceRow row,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityBridgePreviewItem
(
    AcceptedIdentityBridgePreviewRunId,
    SourceIdentityResolutionPreviewRunId,
    FacultyIdentityCandidateDecisionId,
    FacultyId,
    FacultyDisplayName,
    FacultyIdentityCandidateSummaryId,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    BestIdentityEvidenceScore,
    LocalAuthorId,
    ExistingBridgeCandidateId,
    ExistingBridgeCandidateStatus,
    ExistingBridgeCandidateMatchScore,
    ExistingAcceptedAuthorDecisionId,
    CurrentAuthorDecisionCount,
    ProposedCandidateAction,
    ProposedDecisionAction,
    PreviewStatus,
    ProposedNextAction,
    EvidenceSummary,
    EvidenceJson,
    CreatedAtUtc
)
VALUES
(
    @PreviewRunId,
    @SourcePreviewRunId,
    @IdentityDecisionId,
    @FacultyId,
    @FacultyDisplayName,
    @IdentitySummaryId,
    @AuthorOpenAlexId,
    @AuthorOpenAlexShortId,
    @CandidateDisplayName,
    @CandidateOrcid,
    @IdentityScore,
    @LocalAuthorId,
    @ExistingBridgeCandidateId,
    @ExistingBridgeCandidateStatus,
    @ExistingBridgeCandidateMatchScore,
    @ExistingAcceptedAuthorDecisionId,
    @CurrentAuthorDecisionCount,
    @ProposedCandidateAction,
    @ProposedDecisionAction,
    @PreviewStatus,
    @ProposedNextAction,
    @EvidenceSummary,
    @EvidenceJson,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@SourcePreviewRunId", row.SourceIdentityResolutionPreviewRunId);
        command.Parameters.AddWithValue("@IdentityDecisionId", row.FacultyIdentityCandidateDecisionId);
        command.Parameters.AddWithValue("@FacultyId", row.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", row.FacultyDisplayName);
        AddNullable(command, "@IdentitySummaryId", SqlDbType.BigInt, row.FacultyIdentityCandidateSummaryId);
        command.Parameters.AddWithValue("@AuthorOpenAlexId", row.CandidateAuthorOpenAlexId);
        AddNullable(command, "@AuthorOpenAlexShortId", SqlDbType.VarChar, 64, row.CandidateAuthorOpenAlexShortId);
        AddNullable(command, "@CandidateDisplayName", SqlDbType.NVarChar, 512, row.CandidateDisplayName);
        AddNullable(command, "@CandidateOrcid", SqlDbType.NVarChar, 256, row.CandidateOrcid);
        AddNullable(command, "@IdentityScore", SqlDbType.Decimal, row.BestIdentityEvidenceScore);
        AddNullable(command, "@LocalAuthorId", SqlDbType.BigInt, row.LocalAuthorId);
        AddNullable(command, "@ExistingBridgeCandidateId", SqlDbType.BigInt, row.ExistingBridgeCandidateId);
        AddNullable(command, "@ExistingBridgeCandidateStatus", SqlDbType.VarChar, 100, row.ExistingBridgeCandidateStatus);
        AddNullable(command, "@ExistingBridgeCandidateMatchScore", SqlDbType.Decimal, row.ExistingBridgeCandidateMatchScore);
        AddNullable(command, "@ExistingAcceptedAuthorDecisionId", SqlDbType.BigInt, row.ExistingAcceptedAuthorDecisionId);
        command.Parameters.AddWithValue("@CurrentAuthorDecisionCount", row.CurrentAuthorDecisionCount);
        command.Parameters.AddWithValue("@ProposedCandidateAction", row.ProposedCandidateAction);
        command.Parameters.AddWithValue("@ProposedDecisionAction", row.ProposedDecisionAction);
        command.Parameters.AddWithValue("@PreviewStatus", row.PreviewStatus);
        command.Parameters.AddWithValue("@ProposedNextAction", row.ProposedNextAction);
        command.Parameters.AddWithValue("@EvidenceSummary", row.EvidenceSummary);
        command.Parameters.AddWithValue("@EvidenceJson", row.EvidenceJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        long previewRunId,
        int selectedAcceptedIdentities,
        int candidateInserts,
        int candidateUpdates,
        int decisionInserts,
        int alreadyAccepted,
        int missingSummary,
        int missingLocalAuthor,
        AcceptedIdentityBridgePreviewSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(
            previewRunId,
            "Completed",
            selectedAcceptedIdentities,
            candidateInserts,
            candidateUpdates,
            decisionInserts,
            alreadyAccepted,
            missingSummary,
            missingLocalAuthor,
            safetyAfter,
            summary,
            null,
            cancellationToken);
    }

    public async Task FailRunAsync(
        long previewRunId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(
            previewRunId,
            "Failed",
            0, 0, 0, 0, 0, 0, 0,
            null,
            null,
            exception.ToString(),
            cancellationToken);
    }

    private async Task UpdateRunAsync(
        long previewRunId,
        string status,
        int selectedAcceptedIdentities,
        int candidateInserts,
        int candidateUpdates,
        int decisionInserts,
        int alreadyAccepted,
        int missingSummary,
        int missingLocalAuthor,
        AcceptedIdentityBridgePreviewSafetyCounts? safetyAfter,
        string? summary,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityBridgePreviewRun
SET
    SelectedAcceptedIdentityCount = @SelectedAcceptedIdentityCount,
    ReadyCandidateInsertCount = @ReadyCandidateInsertCount,
    ReadyCandidateUpdateCount = @ReadyCandidateUpdateCount,
    ReadyDecisionInsertCount = @ReadyDecisionInsertCount,
    AlreadyAcceptedDecisionCount = @AlreadyAcceptedDecisionCount,
    MissingIdentitySummaryCount = @MissingIdentitySummaryCount,
    MissingLocalAuthorCount = @MissingLocalAuthorCount,
    AuthorCandidateRowsAfter = @AuthorCandidateRowsAfter,
    AuthorDecisionRowsAfter = @AuthorDecisionRowsAfter,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    RunStatus = @Status,
    SummaryText = @Summary,
    ErrorText = @Error,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityBridgePreviewRunId = @PreviewRunId;";

        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@SelectedAcceptedIdentityCount", selectedAcceptedIdentities);
        command.Parameters.AddWithValue("@ReadyCandidateInsertCount", candidateInserts);
        command.Parameters.AddWithValue("@ReadyCandidateUpdateCount", candidateUpdates);
        command.Parameters.AddWithValue("@ReadyDecisionInsertCount", decisionInserts);
        command.Parameters.AddWithValue("@AlreadyAcceptedDecisionCount", alreadyAccepted);
        command.Parameters.AddWithValue("@MissingIdentitySummaryCount", missingSummary);
        command.Parameters.AddWithValue("@MissingLocalAuthorCount", missingLocalAuthor);
        command.Parameters.AddWithValue("@AuthorCandidateRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.AuthorCandidateRows);
        command.Parameters.AddWithValue("@AuthorDecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.AuthorDecisionRows);
        command.Parameters.AddWithValue("@PublicationCandidateRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.PublicationDecisionRows);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@Summary", (object?)summary ?? DBNull.Value);
        command.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.AcceptedIdentityBridgePreviewItem', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException("PATCH017B4 preview tables do not exist. Run sql/378_V3E_PATCH017B4_AcceptedIdentityBridgePreview_SQL2016.sql first.");
        }
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? GetNullableInt64(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, long? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, decimal? value)
    {
        var parameter = command.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal)
        {
            parameter.Precision = 18;
            parameter.Scale = 4;
        }
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
