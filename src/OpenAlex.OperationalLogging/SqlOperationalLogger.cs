using Microsoft.Extensions.Options;
using OpenAlex.Data;

namespace OpenAlex.OperationalLogging;

public sealed class SqlOperationalLogger : IOperationalLogger
{
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly OperationalLoggingOptions _options;

    public SqlOperationalLogger(SqlConnectionFactory connectionFactory, IOptions<OperationalLoggingOptions> options)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
    }

    public Task LogInfoAsync(string category, string eventName, string message, Guid? correlationId = null, CancellationToken cancellationToken = default)
    {
        return InsertAsync("Info", category, eventName, message, null, correlationId, cancellationToken);
    }

    public Task LogWarningAsync(string category, string eventName, string message, Guid? correlationId = null, CancellationToken cancellationToken = default)
    {
        return InsertAsync("Warning", category, eventName, message, null, correlationId, cancellationToken);
    }

    public Task LogErrorAsync(string category, string eventName, string message, Exception? exception = null, Guid? correlationId = null, CancellationToken cancellationToken = default)
    {
        return InsertAsync("Error", category, eventName, message, exception, correlationId, cancellationToken);
    }

    private async Task InsertAsync(string level, string category, string eventName, string message, Exception? exception, Guid? correlationId, CancellationToken cancellationToken)
    {
        if (!_options.EnableDatabaseLogging)
        {
            return;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
BEGIN
    INSERT INTO openalex.OperationalLog
    (
        CorrelationId,
        SourceApplication,
        EnvironmentName,
        LogLevel,
        Category,
        EventName,
        Message,
        ExceptionType,
        ExceptionMessage,
        ExceptionStackTrace,
        MachineName,
        CreatedAtUtc
    )
    VALUES
    (
        @CorrelationId,
        @SourceApplication,
        @EnvironmentName,
        @LogLevel,
        @Category,
        @EventName,
        @Message,
        @ExceptionType,
        @ExceptionMessage,
        @ExceptionStackTrace,
        @MachineName,
        SYSUTCDATETIME()
    );
END";

        command.Parameters.AddWithValue("@CorrelationId", (object?)correlationId ?? Guid.NewGuid());
        command.Parameters.AddWithValue("@SourceApplication", _options.SourceApplication);
        command.Parameters.AddWithValue("@EnvironmentName", _options.EnvironmentName);
        command.Parameters.AddWithValue("@LogLevel", level);
        command.Parameters.AddWithValue("@Category", category);
        command.Parameters.AddWithValue("@EventName", eventName);
        command.Parameters.AddWithValue("@Message", message);
        command.Parameters.AddWithValue("@ExceptionType", (object?)exception?.GetType().FullName ?? DBNull.Value);
        command.Parameters.AddWithValue("@ExceptionMessage", (object?)exception?.Message ?? DBNull.Value);
        command.Parameters.AddWithValue("@ExceptionStackTrace", (object?)exception?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@MachineName", Environment.MachineName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
