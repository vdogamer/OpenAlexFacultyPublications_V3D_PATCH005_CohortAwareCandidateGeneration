using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class SchedulerRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public SchedulerRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SchedulerDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        return new SchedulerDashboardSnapshot
        {
            Jobs = await GetJobDefinitionsAsync(cancellationToken),
            RecentRequests = await GetRecentRequestsAsync(100, cancellationToken)
        };
    }

    public async Task<IReadOnlyList<SchedulerJobDefinition>> GetJobDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<SchedulerJobDefinition>();
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (!await ObjectExistsAsync(connection, "openalex.JobDefinition", "U", cancellationToken)) return rows;
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT JobDefinitionId, JobName, JobDisplayName, JobCategory, JobDescription, IsEnabled, IsWriteJob,
       RequiresConfirmation, DefaultRunMode, DefaultMaxPages, DefaultMaxRecords, SortOrder
FROM openalex.JobDefinition
ORDER BY SortOrder, JobName;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SchedulerJobDefinition
            {
                JobDefinitionId = reader.GetInt64(0),
                JobName = reader.GetString(1),
                JobDisplayName = reader.GetString(2),
                JobCategory = reader.GetString(3),
                JobDescription = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsEnabled = reader.GetBoolean(5),
                IsWriteJob = reader.GetBoolean(6),
                RequiresConfirmation = reader.GetBoolean(7),
                DefaultRunMode = reader.GetString(8),
                DefaultMaxPages = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                DefaultMaxRecords = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                SortOrder = reader.GetInt32(11)
            });
        }
        return rows;
    }

    public async Task<SchedulerJobDefinition?> GetJobDefinitionAsync(long jobDefinitionId, CancellationToken cancellationToken = default)
    {
        return (await GetJobDefinitionsAsync(cancellationToken)).FirstOrDefault(job => job.JobDefinitionId == jobDefinitionId);
    }

    public async Task<IReadOnlyList<JobExecutionRequestListItem>> GetRecentRequestsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        var rows = new List<JobExecutionRequestListItem>();
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (!await ObjectExistsAsync(connection, "openalex.JobExecutionRequest", "U", cancellationToken)) return rows;
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    JobExecutionRequestId, JobDefinitionId, JobName, RequestStatus, RequestedBy, RequestedAtUtc,
    RequestReason, RunMode, MaxPages, MaxRecords, StartedAtUtc, FinishedAtUtc, WorkerMachineName,
    WorkerApplicationVersion, IngestionRunId, ErrorMessage
FROM openalex.JobExecutionRequest
ORDER BY JobExecutionRequestId DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadRequest(reader));
        return rows;
    }

    public async Task<long> QueueJobAsync(QueueJobRequestModel request, CancellationToken cancellationToken = default)
    {
        var job = await GetJobDefinitionAsync(request.JobDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException("Job definition was not found.");
        if (!job.IsEnabled) throw new InvalidOperationException("Job definition is disabled.");
        if (job.RequiresConfirmation && !string.Equals(request.ConfirmationText?.Trim(), job.JobName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Confirmation text must match the job name: {job.JobName}");
        }
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.JobExecutionRequest
(JobDefinitionId, JobName, RequestStatus, RequestedBy, RequestedAtUtc, RequestReason, ConfirmationText, RunMode, MaxPages, MaxRecords, CreatedAtUtc, UpdatedAtUtc)
OUTPUT INSERTED.JobExecutionRequestId
VALUES
(@JobDefinitionId, @JobName, 'Queued', @RequestedBy, SYSUTCDATETIME(), @RequestReason, @ConfirmationText, @RunMode, @MaxPages, @MaxRecords, SYSUTCDATETIME(), SYSUTCDATETIME());";
        command.Parameters.AddWithValue("@JobDefinitionId", job.JobDefinitionId);
        command.Parameters.AddWithValue("@JobName", job.JobName);
        command.Parameters.AddWithValue("@RequestedBy", request.RequestedBy);
        command.Parameters.AddWithValue("@RequestReason", DbString(request.RequestReason));
        command.Parameters.AddWithValue("@ConfirmationText", DbString(request.ConfirmationText));
        command.Parameters.AddWithValue("@RunMode", string.IsNullOrWhiteSpace(request.RunMode) ? job.DefaultRunMode : request.RunMode);
        command.Parameters.AddWithValue("@MaxPages", DbInt(request.MaxPages ?? job.DefaultMaxPages));
        command.Parameters.AddWithValue("@MaxRecords", DbInt(request.MaxRecords ?? job.DefaultMaxRecords));
        var requestId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await AddEventAsync(requestId, "RequestQueued", "Information", $"Job '{job.JobName}' was queued by {request.RequestedBy}.", cancellationToken);
        return requestId;
    }

    public async Task<JobExecutionRequestListItem?> ClaimNextQueuedRequestAsync(string workerApplicationVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DECLARE @Claimed TABLE
(
    JobExecutionRequestId BIGINT, JobDefinitionId BIGINT, JobName NVARCHAR(200), RequestStatus NVARCHAR(50),
    RequestedBy NVARCHAR(256), RequestedAtUtc DATETIME2(7), RequestReason NVARCHAR(MAX), RunMode NVARCHAR(50),
    MaxPages INT, MaxRecords INT, StartedAtUtc DATETIME2(7), FinishedAtUtc DATETIME2(7),
    WorkerMachineName NVARCHAR(256), WorkerApplicationVersion NVARCHAR(50), IngestionRunId BIGINT, ErrorMessage NVARCHAR(MAX)
);
;WITH next_request AS
(
    SELECT TOP (1) *
    FROM openalex.JobExecutionRequest WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE RequestStatus = 'Queued'
    ORDER BY RequestedAtUtc, JobExecutionRequestId
)
UPDATE next_request
SET RequestStatus = 'Started', StartedAtUtc = SYSUTCDATETIME(), WorkerMachineName = @MachineName,
    WorkerApplicationVersion = @WorkerApplicationVersion, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT INSERTED.JobExecutionRequestId, INSERTED.JobDefinitionId, INSERTED.JobName, INSERTED.RequestStatus,
       INSERTED.RequestedBy, INSERTED.RequestedAtUtc, INSERTED.RequestReason, INSERTED.RunMode,
       INSERTED.MaxPages, INSERTED.MaxRecords, INSERTED.StartedAtUtc, INSERTED.FinishedAtUtc,
       INSERTED.WorkerMachineName, INSERTED.WorkerApplicationVersion, INSERTED.IngestionRunId, INSERTED.ErrorMessage
INTO @Claimed;
SELECT * FROM @Claimed;";
            command.Parameters.AddWithValue("@MachineName", Environment.MachineName);
            command.Parameters.AddWithValue("@WorkerApplicationVersion", workerApplicationVersion);
            JobExecutionRequestListItem? claimed = null;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken)) claimed = ReadRequest(reader);
            }
            await transaction.CommitAsync(cancellationToken);
            if (claimed is not null)
            {
                await AddEventAsync(claimed.JobExecutionRequestId, "RequestStarted", "Information", $"Worker claimed job '{claimed.JobName}'.", cancellationToken);
            }
            return claimed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task MarkRequestCompletedAsync(long requestId, string message, CancellationToken cancellationToken = default)
    {
        await UpdateRequestStatusAsync(requestId, "Completed", null, cancellationToken);
        await AddEventAsync(requestId, "RequestCompleted", "Information", message, cancellationToken);
    }

    public async Task MarkRequestFailedAsync(long requestId, Exception exception, CancellationToken cancellationToken = default)
    {
        await UpdateRequestStatusAsync(requestId, "Failed", exception.Message, cancellationToken);
        await AddEventAsync(requestId, "RequestFailed", "Error", exception.ToString(), cancellationToken);
    }

    public async Task AddEventAsync(long requestId, string eventName, string eventLevel, string? eventMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (!await ObjectExistsAsync(connection, "openalex.JobExecutionEvent", "U", cancellationToken)) return;
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.JobExecutionEvent (JobExecutionRequestId, EventName, EventLevel, EventMessage, CreatedAtUtc)
VALUES (@JobExecutionRequestId, @EventName, @EventLevel, @EventMessage, SYSUTCDATETIME());";
        command.Parameters.AddWithValue("@JobExecutionRequestId", requestId);
        command.Parameters.AddWithValue("@EventName", eventName);
        command.Parameters.AddWithValue("@EventLevel", eventLevel);
        command.Parameters.AddWithValue("@EventMessage", DbString(eventMessage));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateRequestStatusAsync(long requestId, string status, string? errorMessage, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.JobExecutionRequest
SET RequestStatus = @RequestStatus,
    FinishedAtUtc = CASE WHEN @RequestStatus IN ('Completed','CompletedWithWarnings','Failed','Cancelled','Skipped') THEN SYSUTCDATETIME() ELSE FinishedAtUtc END,
    ErrorMessage = @ErrorMessage,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE JobExecutionRequestId = @JobExecutionRequestId;";
        command.Parameters.AddWithValue("@JobExecutionRequestId", requestId);
        command.Parameters.AddWithValue("@RequestStatus", status);
        command.Parameters.AddWithValue("@ErrorMessage", DbString(errorMessage));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobExecutionRequestListItem ReadRequest(SqlDataReader reader)
    {
        return new JobExecutionRequestListItem
        {
            JobExecutionRequestId = reader.GetInt64(0),
            JobDefinitionId = reader.GetInt64(1),
            JobName = reader.GetString(2),
            RequestStatus = reader.GetString(3),
            RequestedBy = reader.GetString(4),
            RequestedAtUtc = reader.GetDateTime(5),
            RequestReason = reader.IsDBNull(6) ? null : reader.GetString(6),
            RunMode = reader.GetString(7),
            MaxPages = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            MaxRecords = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            StartedAtUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            FinishedAtUtc = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            WorkerMachineName = reader.IsDBNull(12) ? null : reader.GetString(12),
            WorkerApplicationVersion = reader.IsDBNull(13) ? null : reader.GetString(13),
            IngestionRunId = reader.IsDBNull(14) ? null : reader.GetInt64(14),
            ErrorMessage = reader.IsDBNull(15) ? null : reader.GetString(15)
        };
    }

    private static object DbString(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    private static object DbInt(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
