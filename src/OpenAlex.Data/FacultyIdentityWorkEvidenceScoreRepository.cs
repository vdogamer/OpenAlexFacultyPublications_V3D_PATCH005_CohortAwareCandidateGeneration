using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyIdentityWorkEvidenceScoreRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyIdentityWorkEvidenceScoreRepository(SqlConnectionFactory connectionFactory)
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
        await EnsureScoreTableExistsAsync(connection, cancellationToken);

        var deleted = 0;
        foreach (var facultyId in facultyIds.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM openalex.FacultyIdentityWorkEvidenceScore
WHERE FacultyId = @FacultyId;";
            command.Parameters.AddWithValue("@FacultyId", facultyId);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task<IReadOnlyList<FacultyIdentityWorkForScoring>> LoadWorksForFacultyIdsAsync(
        IReadOnlyCollection<long> facultyIds,
        CancellationToken cancellationToken = default)
    {
        if (facultyIds.Count == 0)
        {
            return Array.Empty<FacultyIdentityWorkForScoring>();
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateWork", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidateWork does not exist. Run V3E PATCH002 first.");
        }

        var facultyFilter = string.Join(",", facultyIds.Distinct().OrderBy(id => id).Select(id => id.ToString()));

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT
    w.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.FirstName AS FacultyFirstName,
    f.LastName AS FacultyLastName,
    w.CandidateAuthorOpenAlexId,
    w.CandidateAuthorOpenAlexShortId,
    w.WorkOpenAlexId,
    w.WorkOpenAlexShortId,
    w.MatchedAuthorDisplayName,
    w.WorkDisplayName,
    w.WorkDoi,
    w.PublicationYear,
    w.MatchedAuthorInstitutionsJson,
    w.EvidenceSummary
FROM openalex.FacultyIdentityCandidateWork w
INNER JOIN openalex.Faculty f
    ON w.FacultyId = f.FacultyId
WHERE w.FacultyId IN ({facultyFilter})
ORDER BY
    w.FacultyId,
    w.CandidateAuthorOpenAlexShortId,
    w.PublicationYear DESC,
    w.WorkDisplayName;";

        var rows = new List<FacultyIdentityWorkForScoring>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyIdentityWorkForScoring
            {
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = GetString(reader, "FacultyDisplayName") ?? string.Empty,
                FacultyFirstName = GetString(reader, "FacultyFirstName"),
                FacultyLastName = GetString(reader, "FacultyLastName"),
                CandidateAuthorOpenAlexId = GetString(reader, "CandidateAuthorOpenAlexId") ?? string.Empty,
                CandidateAuthorOpenAlexShortId = GetString(reader, "CandidateAuthorOpenAlexShortId"),
                WorkOpenAlexId = GetString(reader, "WorkOpenAlexId") ?? string.Empty,
                WorkOpenAlexShortId = GetString(reader, "WorkOpenAlexShortId"),
                MatchedAuthorDisplayName = GetString(reader, "MatchedAuthorDisplayName"),
                WorkDisplayName = GetString(reader, "WorkDisplayName"),
                WorkDoi = GetString(reader, "WorkDoi"),
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                MatchedAuthorInstitutionsJson = GetString(reader, "MatchedAuthorInstitutionsJson"),
                EvidenceSummary = GetString(reader, "EvidenceSummary")
            });
        }

        return rows;
    }

    public async Task<FacultyIdentityWorkEvidenceScoreUpsertResult> UpsertAsync(
        FacultyIdentityWorkEvidenceScoreWriteModel score,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureScoreTableExistsAsync(connection, cancellationToken);

        await using var update = connection.CreateCommand();
        update.CommandText = @"
UPDATE openalex.FacultyIdentityWorkEvidenceScore
SET
    CandidateAuthorOpenAlexShortId = @CandidateAuthorOpenAlexShortId,
    WorkOpenAlexShortId = @WorkOpenAlexShortId,
    MatchedAuthorDisplayName = @MatchedAuthorDisplayName,
    WorkDisplayName = @WorkDisplayName,
    PublicationYear = @PublicationYear,
    NameEvidenceScore = @NameEvidenceScore,
    InstitutionEvidenceScore = @InstitutionEvidenceScore,
    DoiEvidenceScore = @DoiEvidenceScore,
    PublicationYearEvidenceScore = @PublicationYearEvidenceScore,
    OverallWorkEvidenceScore = @OverallWorkEvidenceScore,
    WorkEvidenceStatus = @WorkEvidenceStatus,
    EvidenceSummary = @EvidenceSummary,
    EvidenceJson = @EvidenceJson,
    IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND CandidateAuthorOpenAlexId = @CandidateAuthorOpenAlexId
  AND WorkOpenAlexId = @WorkOpenAlexId;";
        AddParameters(update, score);
        var updatedRows = await update.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows > 0)
        {
            return new FacultyIdentityWorkEvidenceScoreUpsertResult { Updated = true };
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO openalex.FacultyIdentityWorkEvidenceScore
(
    FacultyId,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    MatchedAuthorDisplayName,
    WorkDisplayName,
    PublicationYear,
    NameEvidenceScore,
    InstitutionEvidenceScore,
    DoiEvidenceScore,
    PublicationYearEvidenceScore,
    OverallWorkEvidenceScore,
    WorkEvidenceStatus,
    EvidenceSummary,
    EvidenceJson,
    IngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @FacultyId,
    @CandidateAuthorOpenAlexId,
    @CandidateAuthorOpenAlexShortId,
    @WorkOpenAlexId,
    @WorkOpenAlexShortId,
    @MatchedAuthorDisplayName,
    @WorkDisplayName,
    @PublicationYear,
    @NameEvidenceScore,
    @InstitutionEvidenceScore,
    @DoiEvidenceScore,
    @PublicationYearEvidenceScore,
    @OverallWorkEvidenceScore,
    @WorkEvidenceStatus,
    @EvidenceSummary,
    @EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        AddParameters(insert, score);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return new FacultyIdentityWorkEvidenceScoreUpsertResult { Inserted = true };
    }

    private async Task EnsureScoreTableExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityWorkEvidenceScore", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityWorkEvidenceScore does not exist. Run sql/220_V3E_PATCH003_FacultyIdentityWorkEvidenceScore.sql first.");
        }
    }

    private static void AddParameters(SqlCommand command, FacultyIdentityWorkEvidenceScoreWriteModel score)
    {
        command.Parameters.AddWithValue("@FacultyId", score.FacultyId);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", score.CandidateAuthorOpenAlexId);
        AddNullable(command, "@CandidateAuthorOpenAlexShortId", SqlDbType.VarChar, 64, score.CandidateAuthorOpenAlexShortId);
        command.Parameters.AddWithValue("@WorkOpenAlexId", score.WorkOpenAlexId);
        AddNullable(command, "@WorkOpenAlexShortId", SqlDbType.VarChar, 64, score.WorkOpenAlexShortId);
        AddNullable(command, "@MatchedAuthorDisplayName", SqlDbType.NVarChar, 512, score.MatchedAuthorDisplayName);
        AddNullable(command, "@WorkDisplayName", SqlDbType.NVarChar, -1, score.WorkDisplayName);
        AddNullable(command, "@PublicationYear", SqlDbType.Int, score.PublicationYear);
        command.Parameters.AddWithValue("@NameEvidenceScore", score.NameEvidenceScore);
        command.Parameters.AddWithValue("@InstitutionEvidenceScore", score.InstitutionEvidenceScore);
        command.Parameters.AddWithValue("@DoiEvidenceScore", score.DoiEvidenceScore);
        command.Parameters.AddWithValue("@PublicationYearEvidenceScore", score.PublicationYearEvidenceScore);
        command.Parameters.AddWithValue("@OverallWorkEvidenceScore", score.OverallWorkEvidenceScore);
        command.Parameters.AddWithValue("@WorkEvidenceStatus", score.WorkEvidenceStatus);
        AddNullable(command, "@EvidenceSummary", SqlDbType.NVarChar, -1, score.EvidenceSummary);
        AddNullable(command, "@EvidenceJson", SqlDbType.NVarChar, -1, score.EvidenceJson);
        command.Parameters.AddWithValue("@IngestionRunId", score.IngestionRunId);
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

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
