using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class OperationalDashboardRepository
{
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly OperationsRepository _operationsRepository;
    private readonly JobDashboardRepository _jobDashboardRepository;

    public OperationalDashboardRepository(
        SqlConnectionFactory connectionFactory,
        OperationsRepository operationsRepository,
        JobDashboardRepository jobDashboardRepository)
    {
        _connectionFactory = connectionFactory;
        _operationsRepository = operationsRepository;
        _jobDashboardRepository = jobDashboardRepository;
    }

    public async Task<OperationalDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var metrics = new List<OperationalMetricCard>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var facultyCount = await CountRowsAsync(connection, "openalex.Faculty", cancellationToken);
        var authorCount = await CountRowsAsync(connection, "openalex.Author", cancellationToken);
        var workCount = await CountRowsAsync(connection, "openalex.Work", cancellationToken);
        var acceptedPublicationCount = await CountWhereAsync(
            connection,
            "openalex.FacultyPublicationDecision",
            "DecisionStatus = 'Accepted' AND IsCurrent = 1",
            cancellationToken);

        var errorCount = await CountWhereAsync(
            connection,
            "openalex.OperationalLog",
            "LogLevel IN ('Error', 'Critical')",
            cancellationToken);

        var failedRunCount = await CountWhereAsync(
            connection,
            "openalex.IngestionRun",
            "Status IN ('Failed', 'CompletedWithWarnings')",
            cancellationToken);

        metrics.Add(new OperationalMetricCard
        {
            Label = "Faculty",
            Value = facultyCount?.ToString() ?? "n/a",
            Status = facultyCount.GetValueOrDefault() > 0 ? "Ready" : "Warning",
            Detail = "Active source-of-truth faculty mirror/staging rows."
        });

        metrics.Add(new OperationalMetricCard
        {
            Label = "Authors",
            Value = authorCount?.ToString() ?? "n/a",
            Status = authorCount.GetValueOrDefault() > 0 ? "Ready" : "Warning",
            Detail = "OpenAlex author rows available for matching."
        });

        metrics.Add(new OperationalMetricCard
        {
            Label = "Works",
            Value = workCount?.ToString() ?? "n/a",
            Status = workCount.GetValueOrDefault() > 0 ? "Ready" : "Warning",
            Detail = "OpenAlex works/publications available for review."
        });

        metrics.Add(new OperationalMetricCard
        {
            Label = "Accepted Publications",
            Value = acceptedPublicationCount?.ToString() ?? "n/a",
            Status = acceptedPublicationCount.GetValueOrDefault() > 0 ? "Ready" : "Warning",
            Detail = "Current accepted publication decisions."
        });

        metrics.Add(new OperationalMetricCard
        {
            Label = "Operational Errors",
            Value = errorCount?.ToString() ?? "n/a",
            Status = errorCount.GetValueOrDefault() == 0 ? "Ready" : "Danger",
            Detail = "OperationalLog rows where LogLevel is Error/Critical."
        });

        metrics.Add(new OperationalMetricCard
        {
            Label = "Failed/Warning Runs",
            Value = failedRunCount?.ToString() ?? "n/a",
            Status = failedRunCount.GetValueOrDefault() == 0 ? "Ready" : "Warning",
            Detail = "IngestionRun rows with Failed or CompletedWithWarnings status."
        });

        var jobSnapshot = await _jobDashboardRepository.GetSnapshotAsync(cancellationToken);

        return new OperationalDashboardSnapshot
        {
            Metrics = metrics,
            RecentLogs = await _operationsRepository.GetRecentLogsAsync(25, cancellationToken),
            RecentRuns = jobSnapshot.RecentRuns.Take(25).ToList(),
            RecentErrors = (await GetApplicationErrorsAsync(10, cancellationToken)).Rows,
            RecentHealthChecks = (await GetHealthChecksAsync(10, cancellationToken)).Rows
        };
    }

    public async Task<GenericOperationalTable> GetApplicationErrorsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (await ObjectExistsAsync(connection, "openalex.ApplicationError", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Application Errors",
                "openalex.ApplicationError",
                top,
                cancellationToken);
        }

        if (await ObjectExistsAsync(connection, "openalex.OperationalLog", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Operational Log Errors",
                "openalex.OperationalLog",
                top,
                cancellationToken,
                "LogLevel IN ('Error', 'Critical')");
        }

        return EmptyTable("Application Errors", "openalex.ApplicationError");
    }

    public async Task<GenericOperationalTable> GetHealthChecksAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (await ObjectExistsAsync(connection, "openalex.HealthCheckResult", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Health Check Results",
                "openalex.HealthCheckResult",
                top,
                cancellationToken);
        }

        return EmptyTable("Health Check Results", "openalex.HealthCheckResult");
    }

    public async Task<GenericOperationalTable> GetAlertEventsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (await ObjectExistsAsync(connection, "openalex.AlertEvent", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Alert Events",
                "openalex.AlertEvent",
                top,
                cancellationToken);
        }

        return EmptyTable("Alert Events", "openalex.AlertEvent");
    }

    public async Task<GenericOperationalTable> GetIngestionRunsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (await ObjectExistsAsync(connection, "openalex.IngestionRun", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Ingestion Runs",
                "openalex.IngestionRun",
                top,
                cancellationToken);
        }

        return EmptyTable("Ingestion Runs", "openalex.IngestionRun");
    }

    public async Task<GenericOperationalTable> GetRequestLogsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (await ObjectExistsAsync(connection, "openalex.RequestLog", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Request Logs",
                "openalex.RequestLog",
                top,
                cancellationToken);
        }

        return EmptyTable("Request Logs", "openalex.RequestLog");
    }

    public async Task<GenericOperationalTable> GetOperationalLogsAsync(int top = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (await ObjectExistsAsync(connection, "openalex.OperationalLog", "U", cancellationToken))
        {
            return await ReadGenericTableAsync(
                connection,
                "Operational Logs",
                "openalex.OperationalLog",
                top,
                cancellationToken);
        }

        return EmptyTable("Operational Logs", "openalex.OperationalLog");
    }

    private async Task<GenericOperationalTable> ReadGenericTableAsync(
        SqlConnection connection,
        string title,
        string objectName,
        int top,
        CancellationToken cancellationToken,
        string? whereClause = null)
    {
        if (!await ObjectExistsAsync(connection, objectName, "U", cancellationToken))
        {
            return EmptyTable(title, objectName);
        }

        var orderBy = await BuildOrderByAsync(connection, objectName, cancellationToken);
        var safeTop = Math.Clamp(top, 1, 500);

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({safeTop}) *
FROM {objectName}
{(string.IsNullOrWhiteSpace(whereClause) ? string.Empty : "WHERE " + whereClause)}
{orderBy};";

        var rows = new List<GenericOperationalRow>();
        var columns = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new GenericOperationalRow();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Cells[reader.GetName(i)] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
            }

            rows.Add(row);
        }

        return new GenericOperationalTable
        {
            Title = title,
            SourceObject = objectName,
            Columns = columns,
            Rows = rows
        };
    }

    private static GenericOperationalTable EmptyTable(string title, string objectName)
    {
        return new GenericOperationalTable
        {
            Title = title,
            SourceObject = objectName,
            Columns = Array.Empty<string>(),
            Rows = Array.Empty<GenericOperationalRow>()
        };
    }

    private static async Task<string> BuildOrderByAsync(SqlConnection connection, string objectName, CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            "CreatedAtUtc",
            "StartedAtUtc",
            "FinishedAtUtc",
            "UpdatedAtUtc",
            "OperationalLogId",
            "ApplicationErrorId",
            "HealthCheckResultId",
            "AlertEventId",
            "IngestionRunId",
            "RequestLogId"
        };

        foreach (var candidate in candidates)
        {
            if (await ColumnExistsAsync(connection, objectName, candidate, cancellationToken))
            {
                return $"ORDER BY {candidate} DESC";
            }
        }

        return string.Empty;
    }

    private static async Task<long?> CountRowsAsync(SqlConnection connection, string objectName, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, objectName, "U", cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(*) FROM {objectName};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long?> CountWhereAsync(SqlConnection connection, string objectName, string whereClause, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, objectName, "U", cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(*) FROM {objectName} WHERE {whereClause};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string objectName, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN COL_LENGTH(@ObjectName, @ColumnName) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
