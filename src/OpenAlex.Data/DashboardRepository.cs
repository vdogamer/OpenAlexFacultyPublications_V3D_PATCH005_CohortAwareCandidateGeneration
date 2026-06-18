using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class DashboardRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public DashboardRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var snapshot = new DashboardSnapshot
        {
            Institutions = await CountTableAsync(connection, "openalex.Institution", cancellationToken),
            Authors = await CountTableAsync(connection, "openalex.Author", cancellationToken),
            Works = await CountTableAsync(connection, "openalex.Work", cancellationToken),
            Faculty = await CountTableAsync(connection, "openalex.Faculty", cancellationToken),
            AuthorCandidates = await CountTableAsync(connection, "openalex.FacultyAuthorMatchCandidate", cancellationToken),
            AuthorDecisions = await CountTableAsync(connection, "openalex.FacultyAuthorMatchDecision", cancellationToken),
            PublicationCandidates = await CountTableAsync(connection, "openalex.FacultyPublicationCandidate", cancellationToken),
            PublicationDecisions = await CountTableAsync(connection, "openalex.FacultyPublicationDecision", cancellationToken),
            AcceptedPublications = await CountViewAsync(connection, "openalex.vw_FacultyPublicationExport", cancellationToken),
            OperationalErrors = await CountOperationalErrorsAsync(connection, cancellationToken),
            LastIngestionRunAtUtc = await GetMaxDateAsync(connection, "openalex.IngestionRun", "StartedAtUtc", cancellationToken),
            LastOperationalLogAtUtc = await GetMaxDateAsync(connection, "openalex.OperationalLog", "CreatedAtUtc", cancellationToken)
        };

        return snapshot;
    }

    private static async Task<long> CountTableAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, CancellationToken cancellationToken)
    {
        return await CountObjectAsync(connection, objectName, "U", cancellationToken);
    }

    private static async Task<long> CountViewAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, CancellationToken cancellationToken)
    {
        return await CountObjectAsync(connection, objectName, "V", cancellationToken);
    }

    private static async Task<long> CountObjectAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, objectName, objectType, cancellationToken))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(*) FROM {objectName};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> CountOperationalErrorsAsync(Microsoft.Data.SqlClient.SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, "openalex.OperationalLog", "U", cancellationToken))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT_BIG(*)
FROM openalex.OperationalLog
WHERE LogLevel IN ('Error', 'Critical');";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<DateTime?> GetMaxDateAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, string columnName, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, objectName, "U", cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MAX({columnName}) FROM {objectName};";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value == DBNull.Value || value is null ? null : Convert.ToDateTime(value);
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
