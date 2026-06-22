using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyIdentityCandidateRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyIdentityCandidateRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> DeleteForFacultyIdsAsync(
        IReadOnlyCollection<long> facultyIds,
        CancellationToken cancellationToken = default)
    {
        if (facultyIds.Count == 0)
        {
            return 0;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidate", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidate does not exist. Run sql/200_V3E_PATCH001_FacultyIdentityCandidate.sql first.");
        }

        var deleted = 0;
        foreach (var facultyId in facultyIds.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM openalex.FacultyIdentityCandidate
WHERE FacultyId = @FacultyId;";
            command.Parameters.AddWithValue("@FacultyId", facultyId);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task<FacultyIdentityCandidateUpsertResult> UpsertAsync(
        FacultyIdentityCandidateWriteModel candidate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidate", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidate does not exist. Run sql/200_V3E_PATCH001_FacultyIdentityCandidate.sql first.");
        }

        await using var update = connection.CreateCommand();
        update.CommandText = @"
UPDATE openalex.FacultyIdentityCandidate
SET
    CandidateAuthorOpenAlexShortId = @CandidateAuthorOpenAlexShortId,
    CandidateDisplayName = @CandidateDisplayName,
    CandidateOrcid = @CandidateOrcid,
    WorksCount = @WorksCount,
    CitedByCount = @CitedByCount,
    LastKnownInstitutionsJson = @LastKnownInstitutionsJson,
    EvidenceScore = @EvidenceScore,
    EvidenceStatus = @EvidenceStatus,
    EvidenceSummary = @EvidenceSummary,
    EvidenceJson = @EvidenceJson,
    IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND CandidateAuthorOpenAlexId = @CandidateAuthorOpenAlexId
  AND DiscoveryMethod = @DiscoveryMethod;";
        AddParameters(update, candidate);

        var updatedRows = await update.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows > 0)
        {
            return new FacultyIdentityCandidateUpsertResult { Updated = true };
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO openalex.FacultyIdentityCandidate
(
    FacultyId,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    WorksCount,
    CitedByCount,
    LastKnownInstitutionsJson,
    DiscoveryMethod,
    EvidenceScore,
    EvidenceStatus,
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
    @CandidateDisplayName,
    @CandidateOrcid,
    @WorksCount,
    @CitedByCount,
    @LastKnownInstitutionsJson,
    @DiscoveryMethod,
    @EvidenceScore,
    @EvidenceStatus,
    @EvidenceSummary,
    @EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        AddParameters(insert, candidate);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        return new FacultyIdentityCandidateUpsertResult { Inserted = true };
    }

    private static void AddParameters(SqlCommand command, FacultyIdentityCandidateWriteModel candidate)
    {
        command.Parameters.AddWithValue("@FacultyId", candidate.FacultyId);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", candidate.CandidateAuthorOpenAlexId);
        AddNullable(command, "@CandidateAuthorOpenAlexShortId", SqlDbType.VarChar, 64, candidate.CandidateAuthorOpenAlexShortId);
        AddNullable(command, "@CandidateDisplayName", SqlDbType.NVarChar, 512, candidate.CandidateDisplayName);
        AddNullable(command, "@CandidateOrcid", SqlDbType.NVarChar, 256, candidate.CandidateOrcid);
        AddNullable(command, "@WorksCount", SqlDbType.Int, candidate.WorksCount);
        AddNullable(command, "@CitedByCount", SqlDbType.Int, candidate.CitedByCount);
        AddNullable(command, "@LastKnownInstitutionsJson", SqlDbType.NVarChar, -1, candidate.LastKnownInstitutionsJson);
        command.Parameters.AddWithValue("@DiscoveryMethod", candidate.DiscoveryMethod);
        command.Parameters.AddWithValue("@EvidenceScore", candidate.EvidenceScore);
        command.Parameters.AddWithValue("@EvidenceStatus", candidate.EvidenceStatus);
        AddNullable(command, "@EvidenceSummary", SqlDbType.NVarChar, -1, candidate.EvidenceSummary);
        AddNullable(command, "@EvidenceJson", SqlDbType.NVarChar, -1, candidate.EvidenceJson);
        command.Parameters.AddWithValue("@IngestionRunId", candidate.IngestionRunId);
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

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
