using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class FacultyAcademicOnlineMappingRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyAcademicOnlineMappingRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FacultyAcademicOnlineMappingRow?> GetCurrentMappingAsync(
        long facultyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "openalex.FacultyAcademicOnlineAuthorMap",
                "U",
                cancellationToken))
        {
            throw new InvalidOperationException(
                "openalex.FacultyAcademicOnlineAuthorMap does not exist. Run sql/406_V3E_PATCH018_FacultyAcademicOnlineAuthorMap_SQL2016.sql first.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (1)
    f.FacultyId,
    f.DisplayName,
    f.DepartmentName,
    f.PrimaryEmail,
    f.Orcid,
    m.FacultyAcademicOnlineAuthorMapId,
    m.AcademicOnlineAuthorId,
    m.AcademicOnlineName,
    m.AcademicOnlineEmail,
    m.AcademicOnlineOrcidUrl,
    m.MappingSource,
    m.EvidenceNotes,
    m.VerifiedBy,
    m.VerifiedAtUtc
FROM openalex.Faculty f
LEFT JOIN openalex.FacultyAcademicOnlineAuthorMap m
    ON m.FacultyId = f.FacultyId
   AND m.IsCurrent = 1
WHERE f.FacultyId = @FacultyId;";

        command.Parameters.AddWithValue("@FacultyId", facultyId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FacultyAcademicOnlineMappingRow
        {
            FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
            FacultyDisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
            DepartmentName = GetNullableString(reader, "DepartmentName"),
            FacultyEmail = GetNullableString(reader, "PrimaryEmail"),
            FacultyOrcid = GetNullableString(reader, "Orcid"),
            FacultyAcademicOnlineAuthorMapId =
                GetNullableInt64(reader, "FacultyAcademicOnlineAuthorMapId"),
            AcademicOnlineAuthorId =
                GetNullableString(reader, "AcademicOnlineAuthorId"),
            AcademicOnlineName =
                GetNullableString(reader, "AcademicOnlineName"),
            AcademicOnlineEmail =
                GetNullableString(reader, "AcademicOnlineEmail"),
            AcademicOnlineOrcidUrl =
                GetNullableString(reader, "AcademicOnlineOrcidUrl"),
            MappingSource =
                GetNullableString(reader, "MappingSource"),
            EvidenceNotes =
                GetNullableString(reader, "EvidenceNotes"),
            VerifiedBy =
                GetNullableString(reader, "VerifiedBy"),
            VerifiedAtUtc =
                GetNullableDateTime(reader, "VerifiedAtUtc")
        };
    }

    public async Task<FacultyAcademicOnlineSnapshotCandidate?> GetLatestSnapshotCandidateAsync(
        long facultyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "openalex.AcademicOnlineScholarSnapshot",
                "U",
                cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (1)
    s.AcademicOnlineScholarSnapshotId,
    s.FacultyId,
    s.AcademicOnlineAuthorId,
    s.AcademicOnlineName,
    s.AcademicOnlineEmail,
    s.AcademicOnlinePosition,
    s.AcademicOnlineOrcidUrl,
    s.PublicationCount,
    s.SearchRankingScore,
    s.CreatedAtUtc
FROM openalex.AcademicOnlineScholarSnapshot s
WHERE s.FacultyId = @FacultyId
  AND NULLIF(LTRIM(RTRIM(s.AcademicOnlineAuthorId)), '') IS NOT NULL
ORDER BY s.AcademicOnlineScholarSnapshotId DESC;";
        command.Parameters.AddWithValue("@FacultyId", facultyId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FacultyAcademicOnlineSnapshotCandidate
        {
            AcademicOnlineScholarSnapshotId =
                reader.GetInt64(reader.GetOrdinal("AcademicOnlineScholarSnapshotId")),
            FacultyId =
                reader.GetInt64(reader.GetOrdinal("FacultyId")),
            AcademicOnlineAuthorId =
                reader.GetString(reader.GetOrdinal("AcademicOnlineAuthorId")),
            AcademicOnlineName =
                GetNullableString(reader, "AcademicOnlineName"),
            AcademicOnlineEmail =
                GetNullableString(reader, "AcademicOnlineEmail"),
            AcademicOnlinePosition =
                GetNullableString(reader, "AcademicOnlinePosition"),
            AcademicOnlineOrcidUrl =
                GetNullableString(reader, "AcademicOnlineOrcidUrl"),
            PublicationCount =
                GetNullableInt32(reader, "PublicationCount"),
            SearchRankingScore =
                GetNullableDecimal(reader, "SearchRankingScore"),
            CreatedAtUtc =
                reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc"))
        };
    }

    public async Task SetCurrentMappingAsync(
        long facultyId,
        string academicOnlineAuthorId,
        string? academicOnlineName,
        string? academicOnlineEmail,
        string? academicOnlineOrcidUrl,
        string mappingSource,
        string? evidenceNotes,
        string verifiedBy,
        CancellationToken cancellationToken = default)
    {
        if (facultyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(facultyId));
        }

        if (string.IsNullOrWhiteSpace(academicOnlineAuthorId))
        {
            throw new ArgumentException(
                "Academic Online author ID is required.",
                nameof(academicOnlineAuthorId));
        }

        if (string.IsNullOrWhiteSpace(mappingSource))
        {
            throw new ArgumentException("Mapping source is required.", nameof(mappingSource));
        }

        if (string.IsNullOrWhiteSpace(verifiedBy))
        {
            throw new ArgumentException("VerifiedBy is required.", nameof(verifiedBy));
        }

        var authorId = academicOnlineAuthorId.Trim();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "openalex.FacultyAcademicOnlineAuthorMap",
                "U",
                cancellationToken))
        {
            throw new InvalidOperationException(
                "openalex.FacultyAcademicOnlineAuthorMap does not exist.");
        }

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var validateFaculty = connection.CreateCommand())
            {
                validateFaculty.Transaction = transaction;
                validateFaculty.CommandText =
                    "SELECT COUNT(*) FROM openalex.Faculty WHERE FacultyId = @FacultyId;";
                validateFaculty.Parameters.AddWithValue("@FacultyId", facultyId);

                if (Convert.ToInt32(
                        await validateFaculty.ExecuteScalarAsync(cancellationToken)) != 1)
                {
                    throw new InvalidOperationException(
                        $"FacultyId {facultyId} does not exist.");
                }
            }

            await using (var validateAuthor = connection.CreateCommand())
            {
                validateAuthor.Transaction = transaction;
                validateAuthor.CommandText = @"
IF EXISTS
(
    SELECT 1
    FROM openalex.FacultyAcademicOnlineAuthorMap
    WHERE AcademicOnlineAuthorId = @AcademicOnlineAuthorId
      AND IsCurrent = 1
      AND FacultyId <> @FacultyId
)
BEGIN
    RAISERROR('The Academic Online author ID is already mapped to another current faculty record.', 16, 1);
END;";
                validateAuthor.Parameters.AddWithValue("@AcademicOnlineAuthorId", authorId);
                validateAuthor.Parameters.AddWithValue("@FacultyId", facultyId);
                await validateAuthor.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var retire = connection.CreateCommand())
            {
                retire.Transaction = transaction;
                retire.CommandText = @"
UPDATE openalex.FacultyAcademicOnlineAuthorMap
SET IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND IsCurrent = 1
  AND AcademicOnlineAuthorId <> @AcademicOnlineAuthorId;";
                retire.Parameters.AddWithValue("@FacultyId", facultyId);
                retire.Parameters.AddWithValue("@AcademicOnlineAuthorId", authorId);
                await retire.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = @"
IF EXISTS
(
    SELECT 1
    FROM openalex.FacultyAcademicOnlineAuthorMap
    WHERE FacultyId = @FacultyId
      AND AcademicOnlineAuthorId = @AcademicOnlineAuthorId
)
BEGIN
    UPDATE openalex.FacultyAcademicOnlineAuthorMap
    SET
        AcademicOnlineName = @AcademicOnlineName,
        AcademicOnlineEmail = @AcademicOnlineEmail,
        AcademicOnlineOrcidUrl = @AcademicOnlineOrcidUrl,
        MappingSource = @MappingSource,
        EvidenceNotes = @EvidenceNotes,
        IsCurrent = 1,
        VerifiedBy = @VerifiedBy,
        VerifiedAtUtc = SYSUTCDATETIME(),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE FacultyId = @FacultyId
      AND AcademicOnlineAuthorId = @AcademicOnlineAuthorId;
END
ELSE
BEGIN
    INSERT INTO openalex.FacultyAcademicOnlineAuthorMap
    (
        FacultyId,
        AcademicOnlineAuthorId,
        AcademicOnlineName,
        AcademicOnlineEmail,
        AcademicOnlineOrcidUrl,
        MappingSource,
        EvidenceNotes,
        IsCurrent,
        VerifiedBy,
        VerifiedAtUtc,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    VALUES
    (
        @FacultyId,
        @AcademicOnlineAuthorId,
        @AcademicOnlineName,
        @AcademicOnlineEmail,
        @AcademicOnlineOrcidUrl,
        @MappingSource,
        @EvidenceNotes,
        1,
        @VerifiedBy,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END;";

            upsert.Parameters.AddWithValue("@FacultyId", facultyId);
            upsert.Parameters.AddWithValue("@AcademicOnlineAuthorId", authorId);
            AddNullable(upsert, "@AcademicOnlineName", academicOnlineName);
            AddNullable(upsert, "@AcademicOnlineEmail", academicOnlineEmail);
            AddNullable(upsert, "@AcademicOnlineOrcidUrl", academicOnlineOrcidUrl);
            upsert.Parameters.AddWithValue("@MappingSource", mappingSource.Trim());
            AddNullable(upsert, "@EvidenceNotes", evidenceNotes);
            upsert.Parameters.AddWithValue("@VerifiedBy", verifiedBy.Trim());

            await upsert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddNullable(
        SqlCommand command,
        string name,
        string? value)
    {
        var parameter = command.Parameters.Add(name, System.Data.SqlDbType.NVarChar, -1);
        parameter.Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static string? GetNullableString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? GetNullableInt64(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}

public sealed class FacultyAcademicOnlineMappingRow
{
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? FacultyEmail { get; set; }
    public string? FacultyOrcid { get; set; }

    public long? FacultyAcademicOnlineAuthorMapId { get; set; }
    public string? AcademicOnlineAuthorId { get; set; }
    public string? AcademicOnlineName { get; set; }
    public string? AcademicOnlineEmail { get; set; }
    public string? AcademicOnlineOrcidUrl { get; set; }
    public string? MappingSource { get; set; }
    public string? EvidenceNotes { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }

    public bool HasCurrentMapping =>
        FacultyAcademicOnlineAuthorMapId.HasValue &&
        !string.IsNullOrWhiteSpace(AcademicOnlineAuthorId);
}

public sealed class FacultyAcademicOnlineSnapshotCandidate
{
    public long AcademicOnlineScholarSnapshotId { get; set; }
    public long FacultyId { get; set; }
    public string AcademicOnlineAuthorId { get; set; } = string.Empty;
    public string? AcademicOnlineName { get; set; }
    public string? AcademicOnlineEmail { get; set; }
    public string? AcademicOnlinePosition { get; set; }
    public string? AcademicOnlineOrcidUrl { get; set; }
    public int? PublicationCount { get; set; }
    public decimal? SearchRankingScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
