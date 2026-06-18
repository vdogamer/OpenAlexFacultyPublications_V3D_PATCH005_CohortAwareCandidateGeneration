using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class ReportingRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public ReportingRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AcceptedPublicationDataRow>> GetAcceptedPublicationsAsync(int top = 500, CancellationToken cancellationToken = default)
    {
        var rows = new List<AcceptedPublicationDataRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.vw_FacultyPublicationExport", "V", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 5000)})
    FacultyDisplayName,
    PrimaryEmail,
    DepartmentName,
    DivisionName,
    AppointmentTitle,
    AuthorDisplayName,
    AuthorOpenAlexId,
    AuthorOrcid,
    WorkTitle,
    PublicationYear,
    PublicationDate,
    WorkDoi,
    DoiUrl,
    WorkOpenAlexId,
    WorkType,
    CitedByCount,
    DecisionStatus,
    ReviewedBy,
    ReviewedAtUtc
FROM openalex.vw_FacultyPublicationExport
ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcceptedPublicationDataRow
            {
                FacultyDisplayName = reader.GetString(0),
                PrimaryEmail = reader.IsDBNull(1) ? null : reader.GetString(1),
                DepartmentName = reader.IsDBNull(2) ? null : reader.GetString(2),
                DivisionName = reader.IsDBNull(3) ? null : reader.GetString(3),
                AppointmentTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                AuthorDisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
                AuthorOpenAlexId = reader.IsDBNull(6) ? null : reader.GetString(6),
                AuthorOrcid = reader.IsDBNull(7) ? null : reader.GetString(7),
                WorkTitle = reader.GetString(8),
                PublicationYear = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                PublicationDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                WorkDoi = reader.IsDBNull(11) ? null : reader.GetString(11),
                DoiUrl = reader.IsDBNull(12) ? null : reader.GetString(12),
                WorkOpenAlexId = reader.IsDBNull(13) ? null : reader.GetString(13),
                WorkType = reader.IsDBNull(14) ? null : reader.GetString(14),
                CitedByCount = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                DecisionStatus = reader.IsDBNull(16) ? null : reader.GetString(16),
                ReviewedBy = reader.IsDBNull(17) ? null : reader.GetString(17),
                ReviewedAtUtc = reader.IsDBNull(18) ? null : reader.GetDateTime(18)
            });
        }

        return rows;
    }

    private static async Task<bool> ObjectExistsAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
