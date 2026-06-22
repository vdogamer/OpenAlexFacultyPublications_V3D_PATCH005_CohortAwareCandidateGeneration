using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyIdentityCandidateWorkRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyIdentityCandidateWorkRepository(SqlConnectionFactory connectionFactory)
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
        await EnsureTableExistsAsync(connection, cancellationToken);

        var deleted = 0;
        foreach (var facultyId in facultyIds.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM openalex.FacultyIdentityCandidateWork
WHERE FacultyId = @FacultyId;";
            command.Parameters.AddWithValue("@FacultyId", facultyId);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task<IReadOnlyList<FacultyIdentityCandidateForWorkExpansion>> LoadCandidatesForFacultyIdsAsync(
        IReadOnlyCollection<long> facultyIds,
        decimal minimumEvidenceScore,
        bool includeWeakPossibleMatches,
        CancellationToken cancellationToken = default)
    {
        if (facultyIds.Count == 0)
        {
            return Array.Empty<FacultyIdentityCandidateForWorkExpansion>();
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidate", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidate does not exist. Run V3E PATCH001 first.");
        }

        var facultyFilter = string.Join(",", facultyIds.Distinct().OrderBy(id => id).Select(id => id.ToString()));
        var allowedTypePredicate = includeWeakPossibleMatches
            ? "cr.CandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment', 'WeakPossibleMatch')"
            : "cr.CandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment')";

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
GroupedCandidates AS
(
    SELECT
        cr.FacultyId,
        cr.FacultyDisplayName,
        cr.CandidateAuthorOpenAlexId,
        cr.CandidateAuthorOpenAlexShortId,
        MAX(cr.CandidateDisplayName) AS CandidateDisplayName,
        MAX(cr.CandidateOrcid) AS CandidateOrcid,
        MAX(cr.WorksCount) AS WorksCount,
        MAX(cr.EvidenceScore) AS BestEvidenceScore,
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
    WHERE cr.EvidenceScore >= @MinimumEvidenceScore
      AND {allowedTypePredicate}
    GROUP BY
        cr.FacultyId,
        cr.FacultyDisplayName,
        cr.CandidateAuthorOpenAlexId,
        cr.CandidateAuthorOpenAlexShortId
)
SELECT
    FacultyId,
    FacultyDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    WorksCount,
    BestEvidenceScore,
    CASE BestEvidenceStatusRank
        WHEN 5 THEN 'StrongIdentifierCandidate'
        WHEN 4 THEN 'CandidateForReview'
        WHEN 3 THEN 'PossibleNameCandidate'
        WHEN 2 THEN 'WeakNameCandidate'
        ELSE 'NeedsMoreEvidence'
    END AS BestEvidenceStatus,
    CASE CandidateTypeRank
        WHEN 4 THEN 'PrimaryIdentityCandidate'
        WHEN 3 THEN 'PossibleProfileFragment'
        WHEN 2 THEN 'WeakPossibleMatch'
        WHEN 1 THEN 'LikelyFalsePositive'
        ELSE '(not tagged)'
    END AS BestCandidateType
FROM GroupedCandidates
ORDER BY FacultyId, BestEvidenceScore DESC, CandidateDisplayName;";
        command.Parameters.AddWithValue("@MinimumEvidenceScore", minimumEvidenceScore);

        var candidates = new List<FacultyIdentityCandidateForWorkExpansion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new FacultyIdentityCandidateForWorkExpansion
            {
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = GetString(reader, "FacultyDisplayName") ?? string.Empty,
                CandidateAuthorOpenAlexId = GetString(reader, "CandidateAuthorOpenAlexId") ?? string.Empty,
                CandidateAuthorOpenAlexShortId = GetString(reader, "CandidateAuthorOpenAlexShortId"),
                CandidateDisplayName = GetString(reader, "CandidateDisplayName"),
                CandidateOrcid = GetString(reader, "CandidateOrcid"),
                WorksCount = GetNullableInt32(reader, "WorksCount"),
                BestEvidenceScore = reader.GetDecimal(reader.GetOrdinal("BestEvidenceScore")),
                BestEvidenceStatus = GetString(reader, "BestEvidenceStatus") ?? string.Empty,
                BestCandidateType = GetString(reader, "BestCandidateType") ?? string.Empty
            });
        }

        return candidates;
    }

    public async Task<FacultyIdentityCandidateWorkUpsertResult> UpsertAsync(
        FacultyIdentityCandidateWorkWriteModel work,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTableExistsAsync(connection, cancellationToken);

        await using var update = connection.CreateCommand();
        update.CommandText = @"
UPDATE openalex.FacultyIdentityCandidateWork
SET
    CandidateAuthorOpenAlexShortId = @CandidateAuthorOpenAlexShortId,
    WorkOpenAlexShortId = @WorkOpenAlexShortId,
    WorkDisplayName = @WorkDisplayName,
    WorkDoi = @WorkDoi,
    PublicationYear = @PublicationYear,
    PublicationDate = @PublicationDate,
    WorkType = @WorkType,
    MatchedAuthorDisplayName = @MatchedAuthorDisplayName,
    MatchedAuthorPosition = @MatchedAuthorPosition,
    MatchedAuthorInstitutionsJson = @MatchedAuthorInstitutionsJson,
    AuthorshipsJson = @AuthorshipsJson,
    PrimaryLocationJson = @PrimaryLocationJson,
    LocationsJson = @LocationsJson,
    WorkRawJson = @WorkRawJson,
    EvidenceSummary = @EvidenceSummary,
    IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND CandidateAuthorOpenAlexId = @CandidateAuthorOpenAlexId
  AND WorkOpenAlexId = @WorkOpenAlexId;";
        AddParameters(update, work);

        var updatedRows = await update.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows > 0)
        {
            return new FacultyIdentityCandidateWorkUpsertResult { Updated = true };
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO openalex.FacultyIdentityCandidateWork
(
    FacultyId,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkDisplayName,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    MatchedAuthorDisplayName,
    MatchedAuthorPosition,
    MatchedAuthorInstitutionsJson,
    AuthorshipsJson,
    PrimaryLocationJson,
    LocationsJson,
    WorkRawJson,
    EvidenceSummary,
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
    @WorkDisplayName,
    @WorkDoi,
    @PublicationYear,
    @PublicationDate,
    @WorkType,
    @MatchedAuthorDisplayName,
    @MatchedAuthorPosition,
    @MatchedAuthorInstitutionsJson,
    @AuthorshipsJson,
    @PrimaryLocationJson,
    @LocationsJson,
    @WorkRawJson,
    @EvidenceSummary,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        AddParameters(insert, work);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        return new FacultyIdentityCandidateWorkUpsertResult { Inserted = true };
    }

    private async Task EnsureTableExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateWork", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyIdentityCandidateWork does not exist. Run sql/210_V3E_PATCH002_FacultyIdentityCandidateWork.sql first.");
        }
    }

    private static void AddParameters(SqlCommand command, FacultyIdentityCandidateWorkWriteModel work)
    {
        command.Parameters.AddWithValue("@FacultyId", work.FacultyId);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", work.CandidateAuthorOpenAlexId);
        AddNullable(command, "@CandidateAuthorOpenAlexShortId", SqlDbType.VarChar, 64, work.CandidateAuthorOpenAlexShortId);
        command.Parameters.AddWithValue("@WorkOpenAlexId", work.WorkOpenAlexId);
        AddNullable(command, "@WorkOpenAlexShortId", SqlDbType.VarChar, 64, work.WorkOpenAlexShortId);
        AddNullable(command, "@WorkDisplayName", SqlDbType.NVarChar, -1, work.WorkDisplayName);
        AddNullable(command, "@WorkDoi", SqlDbType.NVarChar, 512, work.WorkDoi);
        AddNullable(command, "@PublicationYear", SqlDbType.Int, work.PublicationYear);
        AddNullable(command, "@PublicationDate", SqlDbType.Date, work.PublicationDate);
        AddNullable(command, "@WorkType", SqlDbType.NVarChar, 100, work.WorkType);
        AddNullable(command, "@MatchedAuthorDisplayName", SqlDbType.NVarChar, 512, work.MatchedAuthorDisplayName);
        AddNullable(command, "@MatchedAuthorPosition", SqlDbType.NVarChar, 50, work.MatchedAuthorPosition);
        AddNullable(command, "@MatchedAuthorInstitutionsJson", SqlDbType.NVarChar, -1, work.MatchedAuthorInstitutionsJson);
        AddNullable(command, "@AuthorshipsJson", SqlDbType.NVarChar, -1, work.AuthorshipsJson);
        AddNullable(command, "@PrimaryLocationJson", SqlDbType.NVarChar, -1, work.PrimaryLocationJson);
        AddNullable(command, "@LocationsJson", SqlDbType.NVarChar, -1, work.LocationsJson);
        AddNullable(command, "@WorkRawJson", SqlDbType.NVarChar, -1, work.WorkRawJson);
        AddNullable(command, "@EvidenceSummary", SqlDbType.NVarChar, -1, work.EvidenceSummary);
        command.Parameters.AddWithValue("@IngestionRunId", work.IngestionRunId);
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

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, DateTime? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value.Date : DBNull.Value;
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
