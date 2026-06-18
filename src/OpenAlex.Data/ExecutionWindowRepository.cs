using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class ExecutionWindowRepository
{
    private readonly SqlConnectionFactory _connectionFactory;
    public ExecutionWindowRepository(SqlConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<JobExecutionWindow>> GetWindowsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<JobExecutionWindow>();
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (!await ObjectExistsAsync(connection, "openalex.JobExecutionWindow", "U", cancellationToken)) return rows;
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT JobExecutionWindowId,WindowName,WindowDescription,AppliesToJobName,IsEnabled,DaysOfWeekCsv,StartTimeLocal,EndTimeLocal,TimeZoneId,AllowFullLoad,MaxRequestsPerWindow FROM openalex.JobExecutionWindow ORDER BY IsEnabled DESC, WindowName;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new JobExecutionWindow
            {
                JobExecutionWindowId = reader.GetInt64(0), WindowName = reader.GetString(1), WindowDescription = reader.IsDBNull(2) ? null : reader.GetString(2), AppliesToJobName = reader.IsDBNull(3) ? null : reader.GetString(3), IsEnabled = reader.GetBoolean(4), DaysOfWeekCsv = reader.GetString(5), StartTimeLocal = reader.GetTimeSpan(6), EndTimeLocal = reader.GetTimeSpan(7), TimeZoneId = reader.GetString(8), AllowFullLoad = reader.GetBoolean(9), MaxRequestsPerWindow = reader.GetInt32(10)
            });
        }
        return rows;
    }

    public async Task<JobExecutionWindow?> GetWindowAsync(long id, CancellationToken cancellationToken = default) => (await GetWindowsAsync(cancellationToken)).FirstOrDefault(w => w.JobExecutionWindowId == id);

    public async Task SaveWindowAsync(JobExecutionWindow window, string changedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (window.JobExecutionWindowId > 0)
        {
            command.CommandText = @"UPDATE openalex.JobExecutionWindow SET WindowName=@WindowName,WindowDescription=@WindowDescription,AppliesToJobName=@AppliesToJobName,IsEnabled=@IsEnabled,DaysOfWeekCsv=@DaysOfWeekCsv,StartTimeLocal=@StartTimeLocal,EndTimeLocal=@EndTimeLocal,TimeZoneId=@TimeZoneId,AllowFullLoad=@AllowFullLoad,MaxRequestsPerWindow=@MaxRequestsPerWindow,UpdatedBy=@ChangedBy,UpdatedAtUtc=SYSUTCDATETIME() WHERE JobExecutionWindowId=@JobExecutionWindowId;";
            command.Parameters.AddWithValue("@JobExecutionWindowId", window.JobExecutionWindowId);
        }
        else
        {
            command.CommandText = @"INSERT INTO openalex.JobExecutionWindow (WindowName,WindowDescription,AppliesToJobName,IsEnabled,DaysOfWeekCsv,StartTimeLocal,EndTimeLocal,TimeZoneId,AllowFullLoad,MaxRequestsPerWindow,CreatedBy,CreatedAtUtc,UpdatedBy,UpdatedAtUtc) VALUES (@WindowName,@WindowDescription,@AppliesToJobName,@IsEnabled,@DaysOfWeekCsv,@StartTimeLocal,@EndTimeLocal,@TimeZoneId,@AllowFullLoad,@MaxRequestsPerWindow,@ChangedBy,SYSUTCDATETIME(),@ChangedBy,SYSUTCDATETIME());";
        }
        command.Parameters.AddWithValue("@WindowName", window.WindowName);
        command.Parameters.AddWithValue("@WindowDescription", string.IsNullOrWhiteSpace(window.WindowDescription) ? DBNull.Value : window.WindowDescription);
        command.Parameters.AddWithValue("@AppliesToJobName", string.IsNullOrWhiteSpace(window.AppliesToJobName) ? DBNull.Value : window.AppliesToJobName);
        command.Parameters.AddWithValue("@IsEnabled", window.IsEnabled);
        command.Parameters.AddWithValue("@DaysOfWeekCsv", window.DaysOfWeekCsv);
        command.Parameters.AddWithValue("@StartTimeLocal", window.StartTimeLocal);
        command.Parameters.AddWithValue("@EndTimeLocal", window.EndTimeLocal);
        command.Parameters.AddWithValue("@TimeZoneId", window.TimeZoneId);
        command.Parameters.AddWithValue("@AllowFullLoad", window.AllowFullLoad);
        command.Parameters.AddWithValue("@MaxRequestsPerWindow", window.MaxRequestsPerWindow);
        command.Parameters.AddWithValue("@ChangedBy", changedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsExecutionAllowedNowAsync(string jobName, string runMode, CancellationToken cancellationToken = default)
    {
        var enabledWindows = (await GetWindowsAsync(cancellationToken)).Where(w => w.IsEnabled).ToList();
        if (enabledWindows.Count == 0) return false;
        var nowLocal = DateTime.Now; var dayName = nowLocal.DayOfWeek.ToString(); var time = nowLocal.TimeOfDay;
        foreach (var window in enabledWindows)
        {
            if (!string.IsNullOrWhiteSpace(window.AppliesToJobName) && !string.Equals(window.AppliesToJobName, jobName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!window.DaysOfWeekCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(day => string.Equals(day, dayName, StringComparison.OrdinalIgnoreCase))) continue;
            if (string.Equals(runMode, "FullLoad", StringComparison.OrdinalIgnoreCase) && !window.AllowFullLoad) continue;
            var inWindow = window.StartTimeLocal < window.EndTimeLocal ? time >= window.StartTimeLocal && time <= window.EndTimeLocal : time >= window.StartTimeLocal || time <= window.EndTimeLocal;
            if (inWindow) return true;
        }
        return false;
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName); command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
