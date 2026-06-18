using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class OperationsRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public OperationsRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<OperationalLogListItem>> GetRecentLogsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        var rows = new List<OperationalLogListItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.OperationalLog", "U", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    OperationalLogId,
    SourceApplication,
    EnvironmentName,
    LogLevel,
    Category,
    EventName,
    Message,
    CreatedAtUtc
FROM openalex.OperationalLog
ORDER BY OperationalLogId DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OperationalLogListItem
            {
                OperationalLogId = reader.GetInt64(0),
                SourceApplication = reader.GetString(1),
                EnvironmentName = reader.IsDBNull(2) ? null : reader.GetString(2),
                LogLevel = reader.GetString(3),
                Category = reader.IsDBNull(4) ? null : reader.GetString(4),
                EventName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Message = reader.GetString(6),
                CreatedAtUtc = reader.GetDateTime(7)
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
