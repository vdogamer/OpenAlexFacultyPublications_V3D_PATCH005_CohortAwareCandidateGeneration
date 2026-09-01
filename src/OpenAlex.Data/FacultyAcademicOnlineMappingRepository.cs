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
