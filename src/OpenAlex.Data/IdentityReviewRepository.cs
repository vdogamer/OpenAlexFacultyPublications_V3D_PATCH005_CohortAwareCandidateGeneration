using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class IdentityReviewRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public IdentityReviewRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<IdentityReviewListItem>> GetIdentityReviewListAsync(
        string? search = null,
        string? triageStatus = null,
        string? decisionStatus = null,
        int top = 200,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<IdentityReviewListItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateSummary", "U", cancellationToken))
        {
            return rows;
        }

        var hasDecisionTable = await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateDecision", "U", cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    s.FacultyIdentityCandidateSummaryId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority,
    s.BestCandidateType,
    s.BestIdentityEvidenceScore,
    s.BestWorkEvidenceScore,
    s.DiscoveryMethodCount,
    s.DiscoveryMethods,
    s.DistinctExpandedWorks,
    s.StrongWorkEvidenceRows,
    s.ModerateWorkEvidenceRows,
    s.ConflictingWorkEvidenceRows,
    d.DecisionStatus AS CurrentDecisionStatus,
    d.ReviewerName AS CurrentReviewerName,
    d.CreatedAtUtc AS CurrentDecisionCreatedAtUtc
FROM openalex.FacultyIdentityCandidateSummary s
LEFT JOIN {(hasDecisionTable ? "openalex.FacultyIdentityCandidateDecision" : "(SELECT CAST(NULL AS BIGINT) AS FacultyId, CAST(NULL AS VARCHAR(128)) AS CandidateAuthorOpenAlexId, CAST(NULL AS VARCHAR(50)) AS DecisionStatus, CAST(NULL AS NVARCHAR(256)) AS ReviewerName, CAST(NULL AS DATETIME2(7)) AS CreatedAtUtc, CAST(0 AS BIT) AS IsCurrent)" )} d
    ON s.FacultyId = d.FacultyId
   AND s.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1
WHERE
    (@Search IS NULL
     OR s.FacultyDisplayName LIKE '%' + @Search + '%'
     OR s.CandidateDisplayName LIKE '%' + @Search + '%'
     OR s.CandidateAuthorOpenAlexShortId LIKE '%' + @Search + '%')
    AND (@TriageStatus IS NULL OR s.IdentityReviewTriageStatus = @TriageStatus)
    AND
    (
        @DecisionStatus IS NULL
        OR (@DecisionStatus = 'Undecided' AND d.DecisionStatus IS NULL)
        OR d.DecisionStatus = @DecisionStatus
    )
ORDER BY
    CASE WHEN d.DecisionStatus IS NULL THEN 0 ELSE 1 END,
    s.ReviewPriority,
    s.FacultyDisplayName,
    s.BestIdentityEvidenceScore DESC,
    s.BestWorkEvidenceScore DESC;";

        command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());
        command.Parameters.AddWithValue("@TriageStatus", string.IsNullOrWhiteSpace(triageStatus) ? DBNull.Value : triageStatus.Trim());
        command.Parameters.AddWithValue("@DecisionStatus", string.IsNullOrWhiteSpace(decisionStatus) ? DBNull.Value : decisionStatus.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadListItem(reader));
        }

        return rows;
    }

    public async Task<IdentityReviewDetail?> GetIdentityReviewDetailAsync(long summaryId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateSummary", "U", cancellationToken))
        {
            return null;
        }

        var hasDecisionTable = await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateDecision", "U", cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT
    s.FacultyIdentityCandidateSummaryId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.IdentityReviewTriageStatus,
    s.ReviewPriority,
    s.BestCandidateType,
    s.BestIdentityEvidenceScore,
    s.BestWorkEvidenceScore,
    s.DiscoveryMethodCount,
    s.DiscoveryMethods,
    s.DistinctExpandedWorks,
    s.StrongWorkEvidenceRows,
    s.ModerateWorkEvidenceRows,
    s.ConflictingWorkEvidenceRows,
    d.DecisionStatus AS CurrentDecisionStatus,
    d.ReviewerName AS CurrentReviewerName,
    d.CreatedAtUtc AS CurrentDecisionCreatedAtUtc,
    s.SummaryText,
    s.EvidenceJson,
    s.WorksCount,
    s.CitedByCount,
    s.ScoredWorkRows,
    s.WeakWorkEvidenceRows,
    s.AverageWorkEvidenceScore,
    s.EarliestPublicationYear,
    s.LatestPublicationYear,
    d.ReviewNotes AS CurrentReviewNotes
FROM openalex.FacultyIdentityCandidateSummary s
LEFT JOIN {(hasDecisionTable ? "openalex.FacultyIdentityCandidateDecision" : "(SELECT CAST(NULL AS BIGINT) AS FacultyId, CAST(NULL AS VARCHAR(128)) AS CandidateAuthorOpenAlexId, CAST(NULL AS VARCHAR(50)) AS DecisionStatus, CAST(NULL AS NVARCHAR(MAX)) AS ReviewNotes, CAST(NULL AS NVARCHAR(256)) AS ReviewerName, CAST(NULL AS DATETIME2(7)) AS CreatedAtUtc, CAST(0 AS BIT) AS IsCurrent)" )} d
    ON s.FacultyId = d.FacultyId
   AND s.CandidateAuthorOpenAlexId = d.CandidateAuthorOpenAlexId
   AND d.IsCurrent = 1
WHERE s.FacultyIdentityCandidateSummaryId = @SummaryId;";
        command.Parameters.AddWithValue("@SummaryId", summaryId);

        IdentityReviewDetail? detail = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                detail = new IdentityReviewDetail
                {
                    Summary = ReadListItem(reader),
                    SummaryText = GetString(reader, "SummaryText"),
                    EvidenceJson = GetString(reader, "EvidenceJson"),
                    WorksCount = GetNullableInt32(reader, "WorksCount"),
                    CitedByCount = GetNullableInt32(reader, "CitedByCount"),
                    ScoredWorkRows = GetInt32(reader, "ScoredWorkRows"),
                    WeakWorkEvidenceRows = GetInt32(reader, "WeakWorkEvidenceRows"),
                    AverageWorkEvidenceScore = GetNullableDecimal(reader, "AverageWorkEvidenceScore"),
                    EarliestPublicationYear = GetNullableInt32(reader, "EarliestPublicationYear"),
                    LatestPublicationYear = GetNullableInt32(reader, "LatestPublicationYear"),
                    CurrentDecisionStatus = GetString(reader, "CurrentDecisionStatus"),
                    CurrentReviewNotes = GetString(reader, "CurrentReviewNotes"),
                    CurrentReviewerName = GetString(reader, "CurrentReviewerName"),
                    CurrentDecisionCreatedAtUtc = GetNullableDateTime(reader, "CurrentDecisionCreatedAtUtc")
                };
            }
        }

        if (detail is null)
        {
            return null;
        }

        detail.WorkEvidence = await GetWorkEvidenceAsync(
            connection,
            detail.Summary.FacultyId,
            detail.Summary.CandidateAuthorOpenAlexId,
            cancellationToken);

        return detail;
    }

    public async Task SaveDecisionAsync(
        long summaryId,
        string decisionStatus,
        string reviewerName,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (decisionStatus is not ("AcceptedIdentity" or "RejectedIdentity" or "NeedsMoreEvidence"))
        {
            throw new InvalidOperationException("Invalid identity review decision status.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateDecision", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidateDecision does not exist. Run sql/240_V3E_PATCH005_FacultyIdentityCandidateDecision.sql first.");
        }

        await using var lookup = connection.CreateCommand();
        lookup.CommandText = @"
SELECT FacultyId, CandidateAuthorOpenAlexId, CandidateAuthorOpenAlexShortId
FROM openalex.FacultyIdentityCandidateSummary
WHERE FacultyIdentityCandidateSummaryId = @SummaryId;";
        lookup.Parameters.AddWithValue("@SummaryId", summaryId);

        long facultyId;
        string candidateAuthorOpenAlexId;
        string? candidateAuthorOpenAlexShortId;
        await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Identity candidate summary row was not found.");
            }

            facultyId = reader.GetInt64(0);
            candidateAuthorOpenAlexId = reader.GetString(1);
            candidateAuthorOpenAlexShortId = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyIdentityCandidateDecision
SET IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND CandidateAuthorOpenAlexId = @CandidateAuthorOpenAlexId
  AND IsCurrent = 1;

INSERT INTO openalex.FacultyIdentityCandidateDecision
(
    FacultyId,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    DecisionStatus,
    ReviewNotes,
    ReviewerName,
    IsCurrent,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @FacultyId,
    @CandidateAuthorOpenAlexId,
    @CandidateAuthorOpenAlexShortId,
    @DecisionStatus,
    @ReviewNotes,
    @ReviewerName,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", candidateAuthorOpenAlexId);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexShortId", string.IsNullOrWhiteSpace(candidateAuthorOpenAlexShortId) ? DBNull.Value : candidateAuthorOpenAlexShortId);
        command.Parameters.AddWithValue("@DecisionStatus", decisionStatus);
        command.Parameters.AddWithValue("@ReviewNotes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim());
        command.Parameters.AddWithValue("@ReviewerName", string.IsNullOrWhiteSpace(reviewerName) ? "local.demo.reviewer" : reviewerName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<IdentityReviewWorkEvidenceItem>> GetWorkEvidenceAsync(
        SqlConnection connection,
        long facultyId,
        string candidateAuthorOpenAlexId,
        CancellationToken cancellationToken)
    {
        var rows = new List<IdentityReviewWorkEvidenceItem>();

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityWorkEvidenceScore", "U", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (100)
    WorkOpenAlexShortId,
    WorkOpenAlexId,
    WorkDisplayName,
    PublicationYear,
    MatchedAuthorDisplayName,
    WorkEvidenceStatus,
    OverallWorkEvidenceScore,
    NameEvidenceScore,
    InstitutionEvidenceScore,
    DoiEvidenceScore,
    PublicationYearEvidenceScore,
    EvidenceSummary
FROM openalex.FacultyIdentityWorkEvidenceScore
WHERE FacultyId = @FacultyId
  AND CandidateAuthorOpenAlexId = @CandidateAuthorOpenAlexId
ORDER BY OverallWorkEvidenceScore DESC, PublicationYear DESC, WorkDisplayName;";
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", candidateAuthorOpenAlexId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new IdentityReviewWorkEvidenceItem
            {
                WorkOpenAlexShortId = GetString(reader, "WorkOpenAlexShortId"),
                WorkOpenAlexId = GetString(reader, "WorkOpenAlexId"),
                WorkDisplayName = GetString(reader, "WorkDisplayName"),
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                MatchedAuthorDisplayName = GetString(reader, "MatchedAuthorDisplayName"),
                WorkEvidenceStatus = GetString(reader, "WorkEvidenceStatus"),
                OverallWorkEvidenceScore = GetDecimal(reader, "OverallWorkEvidenceScore"),
                NameEvidenceScore = GetDecimal(reader, "NameEvidenceScore"),
                InstitutionEvidenceScore = GetDecimal(reader, "InstitutionEvidenceScore"),
                DoiEvidenceScore = GetDecimal(reader, "DoiEvidenceScore"),
                PublicationYearEvidenceScore = GetDecimal(reader, "PublicationYearEvidenceScore"),
                EvidenceSummary = GetString(reader, "EvidenceSummary")
            });
        }

        return rows;
    }

    private static IdentityReviewListItem ReadListItem(SqlDataReader reader)
    {
        return new IdentityReviewListItem
        {
            FacultyIdentityCandidateSummaryId = reader.GetInt64(reader.GetOrdinal("FacultyIdentityCandidateSummaryId")),
            FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
            FacultyDisplayName = GetString(reader, "FacultyDisplayName"),
            CandidateAuthorOpenAlexId = GetString(reader, "CandidateAuthorOpenAlexId") ?? string.Empty,
            CandidateAuthorOpenAlexShortId = GetString(reader, "CandidateAuthorOpenAlexShortId"),
            CandidateDisplayName = GetString(reader, "CandidateDisplayName"),
            CandidateOrcid = GetString(reader, "CandidateOrcid"),
            IdentityReviewTriageStatus = GetString(reader, "IdentityReviewTriageStatus"),
            ReviewPriority = GetInt32(reader, "ReviewPriority"),
            BestCandidateType = GetString(reader, "BestCandidateType"),
            BestIdentityEvidenceScore = GetDecimal(reader, "BestIdentityEvidenceScore"),
            BestWorkEvidenceScore = GetNullableDecimal(reader, "BestWorkEvidenceScore"),
            DiscoveryMethodCount = GetInt32(reader, "DiscoveryMethodCount"),
            DiscoveryMethods = GetString(reader, "DiscoveryMethods"),
            DistinctExpandedWorks = GetInt32(reader, "DistinctExpandedWorks"),
            StrongWorkEvidenceRows = GetInt32(reader, "StrongWorkEvidenceRows"),
            ModerateWorkEvidenceRows = GetInt32(reader, "ModerateWorkEvidenceRows"),
            ConflictingWorkEvidenceRows = GetInt32(reader, "ConflictingWorkEvidenceRows"),
            CurrentDecisionStatus = GetString(reader, "CurrentDecisionStatus"),
            CurrentReviewerName = GetString(reader, "CurrentReviewerName"),
            CurrentDecisionCreatedAtUtc = GetNullableDateTime(reader, "CurrentDecisionCreatedAtUtc")
        };
    }

    private static string? GetString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int GetInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static decimal GetDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0m : reader.GetDecimal(ordinal);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
