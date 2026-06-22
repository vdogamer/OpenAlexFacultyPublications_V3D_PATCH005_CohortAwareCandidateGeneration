using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyIdentityCandidateSummaryRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyIdentityCandidateSummaryRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> DeleteForFacultyIdsAsync(IReadOnlyCollection<long> facultyIds, CancellationToken cancellationToken = default)
    {
        if (facultyIds.Count == 0)
        {
            return 0;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureSummaryTableExistsAsync(connection, cancellationToken);

        var deleted = 0;
        foreach (var facultyId in facultyIds.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM openalex.FacultyIdentityCandidateSummary
WHERE FacultyId = @FacultyId;";
            command.Parameters.AddWithValue("@FacultyId", facultyId);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task<IReadOnlyList<FacultyIdentityCandidateSummarySource>> LoadSummarySourcesAsync(
        IReadOnlyCollection<long> facultyIds,
        CancellationToken cancellationToken = default)
    {
        if (facultyIds.Count == 0)
        {
            return Array.Empty<FacultyIdentityCandidateSummarySource>();
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidate", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidate does not exist. Run V3E PATCH001A first.");
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateWork", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidateWork does not exist. Run V3E PATCH002 first.");
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityWorkEvidenceScore", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityWorkEvidenceScore does not exist. Run V3E PATCH003 first.");
        }

        var facultyFilter = string.Join(",", facultyIds.Distinct().OrderBy(id => id).Select(id => id.ToString()));

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
;WITH CandidateRows AS
(
    SELECT
        fic.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        fic.CandidateAuthorOpenAlexId,
        fic.CandidateAuthorOpenAlexShortId,
        fic.CandidateDisplayName,
        fic.CandidateOrcid,
        fic.WorksCount,
        fic.CitedByCount,
        fic.DiscoveryMethod,
        fic.EvidenceScore,
        fic.EvidenceStatus,
        CASE
            WHEN CHARINDEX('CandidateType=', fic.EvidenceSummary) > 0
             AND CHARINDEX(';', fic.EvidenceSummary, CHARINDEX('CandidateType=', fic.EvidenceSummary)) > CHARINDEX('CandidateType=', fic.EvidenceSummary)
            THEN SUBSTRING(
                    fic.EvidenceSummary,
                    CHARINDEX('CandidateType=', fic.EvidenceSummary) + LEN('CandidateType='),
                    CHARINDEX(';', fic.EvidenceSummary, CHARINDEX('CandidateType=', fic.EvidenceSummary)) - (CHARINDEX('CandidateType=', fic.EvidenceSummary) + LEN('CandidateType='))
                 )
            ELSE '(not tagged)'
        END AS CandidateType
    FROM openalex.FacultyIdentityCandidate fic
    INNER JOIN openalex.Faculty f
        ON fic.FacultyId = f.FacultyId
    WHERE fic.FacultyId IN ({facultyFilter})
),
CandidateGrouped AS
(
    SELECT
        cr.FacultyId,
        cr.FacultyDisplayName,
        cr.CandidateAuthorOpenAlexId,
        cr.CandidateAuthorOpenAlexShortId,
        MAX(cr.CandidateDisplayName) AS CandidateDisplayName,
        MAX(cr.CandidateOrcid) AS CandidateOrcid,
        MAX(cr.WorksCount) AS WorksCount,
        MAX(cr.CitedByCount) AS CitedByCount,
        COUNT(*) AS DiscoveryMethodCount,
        MAX(cr.EvidenceScore) AS BestIdentityEvidenceScore,
        MAX(CASE WHEN cr.EvidenceStatus = 'StrongIdentifierCandidate' THEN 5
                 WHEN cr.EvidenceStatus = 'CandidateForReview' THEN 4
                 WHEN cr.EvidenceStatus = 'PossibleNameCandidate' THEN 3
                 WHEN cr.EvidenceStatus = 'WeakNameCandidate' THEN 2
                 ELSE 1 END) AS BestEvidenceStatusRank,
        MAX(CASE WHEN cr.CandidateType = 'PrimaryIdentityCandidate' THEN 4
                 WHEN cr.CandidateType = 'PossibleProfileFragment' THEN 3
                 WHEN cr.CandidateType = 'WeakPossibleMatch' THEN 2
                 WHEN cr.CandidateType = 'LikelyFalsePositive' THEN 1
                 ELSE 0 END) AS CandidateTypeRank
    FROM CandidateRows cr
    GROUP BY
        cr.FacultyId,
        cr.FacultyDisplayName,
        cr.CandidateAuthorOpenAlexId,
        cr.CandidateAuthorOpenAlexShortId
),
WorkGrouped AS
(
    SELECT
        FacultyId,
        CandidateAuthorOpenAlexId,
        COUNT(*) AS ExpandedWorkRows,
        COUNT(DISTINCT WorkOpenAlexId) AS DistinctExpandedWorks,
        MIN(PublicationYear) AS EarliestPublicationYear,
        MAX(PublicationYear) AS LatestPublicationYear
    FROM openalex.FacultyIdentityCandidateWork
    WHERE FacultyId IN ({facultyFilter})
    GROUP BY FacultyId, CandidateAuthorOpenAlexId
),
ScoreGrouped AS
(
    SELECT
        FacultyId,
        CandidateAuthorOpenAlexId,
        COUNT(*) AS ScoredWorkRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'StrongWorkEvidence' THEN 1 ELSE 0 END) AS StrongWorkEvidenceRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'ModerateWorkEvidence' THEN 1 ELSE 0 END) AS ModerateWorkEvidenceRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'WeakWorkEvidence' THEN 1 ELSE 0 END) AS WeakWorkEvidenceRows,
        SUM(CASE WHEN WorkEvidenceStatus = 'ConflictingWorkEvidence' THEN 1 ELSE 0 END) AS ConflictingWorkEvidenceRows,
        AVG(OverallWorkEvidenceScore) AS AverageWorkEvidenceScore,
        MAX(OverallWorkEvidenceScore) AS BestWorkEvidenceScore
    FROM openalex.FacultyIdentityWorkEvidenceScore
    WHERE FacultyId IN ({facultyFilter})
    GROUP BY FacultyId, CandidateAuthorOpenAlexId
)
SELECT
    cg.FacultyId,
    cg.FacultyDisplayName,
    cg.CandidateAuthorOpenAlexId,
    cg.CandidateAuthorOpenAlexShortId,
    cg.CandidateDisplayName,
    cg.CandidateOrcid,
    cg.WorksCount,
    cg.CitedByCount,
    cg.DiscoveryMethodCount,
    STUFF
    (
        (
            SELECT DISTINCT ', ' + cr2.DiscoveryMethod
            FROM CandidateRows cr2
            WHERE cr2.FacultyId = cg.FacultyId
              AND cr2.CandidateAuthorOpenAlexId = cg.CandidateAuthorOpenAlexId
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'),
        1,
        2,
        ''
    ) AS DiscoveryMethods,
    cg.BestIdentityEvidenceScore,
    CASE cg.BestEvidenceStatusRank
        WHEN 5 THEN 'StrongIdentifierCandidate'
        WHEN 4 THEN 'CandidateForReview'
        WHEN 3 THEN 'PossibleNameCandidate'
        WHEN 2 THEN 'WeakNameCandidate'
        ELSE 'NeedsMoreEvidence'
    END AS BestIdentityEvidenceStatus,
    CASE cg.CandidateTypeRank
        WHEN 4 THEN 'PrimaryIdentityCandidate'
        WHEN 3 THEN 'PossibleProfileFragment'
        WHEN 2 THEN 'WeakPossibleMatch'
        WHEN 1 THEN 'LikelyFalsePositive'
        ELSE '(not tagged)'
    END AS BestCandidateType,
    ISNULL(wg.ExpandedWorkRows, 0) AS ExpandedWorkRows,
    ISNULL(wg.DistinctExpandedWorks, 0) AS DistinctExpandedWorks,
    ISNULL(sg.ScoredWorkRows, 0) AS ScoredWorkRows,
    ISNULL(sg.StrongWorkEvidenceRows, 0) AS StrongWorkEvidenceRows,
    ISNULL(sg.ModerateWorkEvidenceRows, 0) AS ModerateWorkEvidenceRows,
    ISNULL(sg.WeakWorkEvidenceRows, 0) AS WeakWorkEvidenceRows,
    ISNULL(sg.ConflictingWorkEvidenceRows, 0) AS ConflictingWorkEvidenceRows,
    sg.AverageWorkEvidenceScore,
    sg.BestWorkEvidenceScore,
    wg.EarliestPublicationYear,
    wg.LatestPublicationYear
FROM CandidateGrouped cg
LEFT JOIN WorkGrouped wg
    ON cg.FacultyId = wg.FacultyId
   AND cg.CandidateAuthorOpenAlexId = wg.CandidateAuthorOpenAlexId
LEFT JOIN ScoreGrouped sg
    ON cg.FacultyId = sg.FacultyId
   AND cg.CandidateAuthorOpenAlexId = sg.CandidateAuthorOpenAlexId
ORDER BY
    cg.FacultyId,
    cg.BestIdentityEvidenceScore DESC,
    sg.BestWorkEvidenceScore DESC,
    cg.CandidateDisplayName;";

        var rows = new List<FacultyIdentityCandidateSummarySource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyIdentityCandidateSummarySource
            {
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = GetString(reader, "FacultyDisplayName"),
                CandidateAuthorOpenAlexId = GetString(reader, "CandidateAuthorOpenAlexId") ?? string.Empty,
                CandidateAuthorOpenAlexShortId = GetString(reader, "CandidateAuthorOpenAlexShortId"),
                CandidateDisplayName = GetString(reader, "CandidateDisplayName"),
                CandidateOrcid = GetString(reader, "CandidateOrcid"),
                WorksCount = GetNullableInt32(reader, "WorksCount"),
                CitedByCount = GetNullableInt32(reader, "CitedByCount"),
                DiscoveryMethodCount = reader.GetInt32(reader.GetOrdinal("DiscoveryMethodCount")),
                DiscoveryMethods = GetString(reader, "DiscoveryMethods"),
                BestIdentityEvidenceScore = reader.GetDecimal(reader.GetOrdinal("BestIdentityEvidenceScore")),
                BestIdentityEvidenceStatus = GetString(reader, "BestIdentityEvidenceStatus"),
                BestCandidateType = GetString(reader, "BestCandidateType"),
                ExpandedWorkRows = reader.GetInt32(reader.GetOrdinal("ExpandedWorkRows")),
                DistinctExpandedWorks = reader.GetInt32(reader.GetOrdinal("DistinctExpandedWorks")),
                ScoredWorkRows = reader.GetInt32(reader.GetOrdinal("ScoredWorkRows")),
                StrongWorkEvidenceRows = reader.GetInt32(reader.GetOrdinal("StrongWorkEvidenceRows")),
                ModerateWorkEvidenceRows = reader.GetInt32(reader.GetOrdinal("ModerateWorkEvidenceRows")),
                WeakWorkEvidenceRows = reader.GetInt32(reader.GetOrdinal("WeakWorkEvidenceRows")),
                ConflictingWorkEvidenceRows = reader.GetInt32(reader.GetOrdinal("ConflictingWorkEvidenceRows")),
                AverageWorkEvidenceScore = GetNullableDecimal(reader, "AverageWorkEvidenceScore"),
                BestWorkEvidenceScore = GetNullableDecimal(reader, "BestWorkEvidenceScore"),
                EarliestPublicationYear = GetNullableInt32(reader, "EarliestPublicationYear"),
                LatestPublicationYear = GetNullableInt32(reader, "LatestPublicationYear")
            });
        }

        return rows;
    }

    public async Task<FacultyIdentityCandidateSummaryUpsertResult> UpsertAsync(
        FacultyIdentityCandidateSummaryWriteModel summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureSummaryTableExistsAsync(connection, cancellationToken);

        await using var update = connection.CreateCommand();
        update.CommandText = @"
UPDATE openalex.FacultyIdentityCandidateSummary
SET
    FacultyDisplayName = @FacultyDisplayName,
    CandidateAuthorOpenAlexShortId = @CandidateAuthorOpenAlexShortId,
    CandidateDisplayName = @CandidateDisplayName,
    CandidateOrcid = @CandidateOrcid,
    WorksCount = @WorksCount,
    CitedByCount = @CitedByCount,
    DiscoveryMethodCount = @DiscoveryMethodCount,
    DiscoveryMethods = @DiscoveryMethods,
    BestIdentityEvidenceScore = @BestIdentityEvidenceScore,
    BestIdentityEvidenceStatus = @BestIdentityEvidenceStatus,
    BestCandidateType = @BestCandidateType,
    ExpandedWorkRows = @ExpandedWorkRows,
    DistinctExpandedWorks = @DistinctExpandedWorks,
    ScoredWorkRows = @ScoredWorkRows,
    StrongWorkEvidenceRows = @StrongWorkEvidenceRows,
    ModerateWorkEvidenceRows = @ModerateWorkEvidenceRows,
    WeakWorkEvidenceRows = @WeakWorkEvidenceRows,
    ConflictingWorkEvidenceRows = @ConflictingWorkEvidenceRows,
    AverageWorkEvidenceScore = @AverageWorkEvidenceScore,
    BestWorkEvidenceScore = @BestWorkEvidenceScore,
    EarliestPublicationYear = @EarliestPublicationYear,
    LatestPublicationYear = @LatestPublicationYear,
    IdentityReviewTriageStatus = @IdentityReviewTriageStatus,
    ReviewPriority = @ReviewPriority,
    SummaryText = @SummaryText,
    EvidenceJson = @EvidenceJson,
    IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND CandidateAuthorOpenAlexId = @CandidateAuthorOpenAlexId;";
        AddParameters(update, summary);
        var updatedRows = await update.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows > 0)
        {
            return new FacultyIdentityCandidateSummaryUpsertResult { Updated = true };
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO openalex.FacultyIdentityCandidateSummary
(
    FacultyId,
    FacultyDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    WorksCount,
    CitedByCount,
    DiscoveryMethodCount,
    DiscoveryMethods,
    BestIdentityEvidenceScore,
    BestIdentityEvidenceStatus,
    BestCandidateType,
    ExpandedWorkRows,
    DistinctExpandedWorks,
    ScoredWorkRows,
    StrongWorkEvidenceRows,
    ModerateWorkEvidenceRows,
    WeakWorkEvidenceRows,
    ConflictingWorkEvidenceRows,
    AverageWorkEvidenceScore,
    BestWorkEvidenceScore,
    EarliestPublicationYear,
    LatestPublicationYear,
    IdentityReviewTriageStatus,
    ReviewPriority,
    SummaryText,
    EvidenceJson,
    IngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @FacultyId,
    @FacultyDisplayName,
    @CandidateAuthorOpenAlexId,
    @CandidateAuthorOpenAlexShortId,
    @CandidateDisplayName,
    @CandidateOrcid,
    @WorksCount,
    @CitedByCount,
    @DiscoveryMethodCount,
    @DiscoveryMethods,
    @BestIdentityEvidenceScore,
    @BestIdentityEvidenceStatus,
    @BestCandidateType,
    @ExpandedWorkRows,
    @DistinctExpandedWorks,
    @ScoredWorkRows,
    @StrongWorkEvidenceRows,
    @ModerateWorkEvidenceRows,
    @WeakWorkEvidenceRows,
    @ConflictingWorkEvidenceRows,
    @AverageWorkEvidenceScore,
    @BestWorkEvidenceScore,
    @EarliestPublicationYear,
    @LatestPublicationYear,
    @IdentityReviewTriageStatus,
    @ReviewPriority,
    @SummaryText,
    @EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        AddParameters(insert, summary);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return new FacultyIdentityCandidateSummaryUpsertResult { Inserted = true };
    }

    private async Task EnsureSummaryTableExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateSummary", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidateSummary does not exist. Run sql/230_V3E_PATCH004_FacultyIdentityCandidateSummary.sql first.");
        }
    }

    private static void AddParameters(SqlCommand command, FacultyIdentityCandidateSummaryWriteModel summary)
    {
        command.Parameters.AddWithValue("@FacultyId", summary.FacultyId);
        AddNullable(command, "@FacultyDisplayName", SqlDbType.NVarChar, 512, summary.FacultyDisplayName);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", summary.CandidateAuthorOpenAlexId);
        AddNullable(command, "@CandidateAuthorOpenAlexShortId", SqlDbType.VarChar, 64, summary.CandidateAuthorOpenAlexShortId);
        AddNullable(command, "@CandidateDisplayName", SqlDbType.NVarChar, 512, summary.CandidateDisplayName);
        AddNullable(command, "@CandidateOrcid", SqlDbType.NVarChar, 256, summary.CandidateOrcid);
        AddNullable(command, "@WorksCount", SqlDbType.Int, summary.WorksCount);
        AddNullable(command, "@CitedByCount", SqlDbType.Int, summary.CitedByCount);
        command.Parameters.AddWithValue("@DiscoveryMethodCount", summary.DiscoveryMethodCount);
        AddNullable(command, "@DiscoveryMethods", SqlDbType.NVarChar, -1, summary.DiscoveryMethods);
        command.Parameters.AddWithValue("@BestIdentityEvidenceScore", summary.BestIdentityEvidenceScore);
        AddNullable(command, "@BestIdentityEvidenceStatus", SqlDbType.VarChar, 50, summary.BestIdentityEvidenceStatus);
        AddNullable(command, "@BestCandidateType", SqlDbType.VarChar, 50, summary.BestCandidateType);
        command.Parameters.AddWithValue("@ExpandedWorkRows", summary.ExpandedWorkRows);
        command.Parameters.AddWithValue("@DistinctExpandedWorks", summary.DistinctExpandedWorks);
        command.Parameters.AddWithValue("@ScoredWorkRows", summary.ScoredWorkRows);
        command.Parameters.AddWithValue("@StrongWorkEvidenceRows", summary.StrongWorkEvidenceRows);
        command.Parameters.AddWithValue("@ModerateWorkEvidenceRows", summary.ModerateWorkEvidenceRows);
        command.Parameters.AddWithValue("@WeakWorkEvidenceRows", summary.WeakWorkEvidenceRows);
        command.Parameters.AddWithValue("@ConflictingWorkEvidenceRows", summary.ConflictingWorkEvidenceRows);
        AddNullable(command, "@AverageWorkEvidenceScore", SqlDbType.Decimal, summary.AverageWorkEvidenceScore);
        AddNullable(command, "@BestWorkEvidenceScore", SqlDbType.Decimal, summary.BestWorkEvidenceScore);
        AddNullable(command, "@EarliestPublicationYear", SqlDbType.Int, summary.EarliestPublicationYear);
        AddNullable(command, "@LatestPublicationYear", SqlDbType.Int, summary.LatestPublicationYear);
        command.Parameters.AddWithValue("@IdentityReviewTriageStatus", summary.IdentityReviewTriageStatus);
        command.Parameters.AddWithValue("@ReviewPriority", summary.ReviewPriority);
        AddNullable(command, "@SummaryText", SqlDbType.NVarChar, -1, summary.SummaryText);
        AddNullable(command, "@EvidenceJson", SqlDbType.NVarChar, -1, summary.EvidenceJson);
        command.Parameters.AddWithValue("@IngestionRunId", summary.IngestionRunId);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, decimal? value)
    {
        var parameter = command.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal)
        {
            parameter.Precision = 9;
            parameter.Scale = 4;
        }
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static string? GetString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
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
