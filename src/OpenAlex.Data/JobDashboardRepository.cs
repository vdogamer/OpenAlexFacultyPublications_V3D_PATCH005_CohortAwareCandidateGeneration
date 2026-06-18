using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class JobDashboardRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public JobDashboardRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<JobDashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return new JobDashboardSnapshot
        {
            Jobs = GetIntegratedJobs(),
            RecentRuns = await GetRecentIngestionRunsAsync(50, cancellationToken)
        };
    }

    public static IReadOnlyList<IntegratedJobDefinition> GetIntegratedJobs()
    {
        return new List<IntegratedJobDefinition>
        {
            new()
            {
                JobName = "InstitutionIngestion",
                JobType = "Ingestion",
                Purpose = "Pull configured institutions from OpenAlex and upsert openalex.Institution.",
                RecommendedRunMode = "TestLoad",
                WritesData = true,
                SortOrder = 10
            },
            new()
            {
                JobName = "AuthorIngestion",
                JobType = "Ingestion",
                Purpose = "Pull authors for configured institutions and upsert openalex.Author.",
                RecommendedRunMode = "TestLoad",
                WritesData = true,
                SortOrder = 20
            },
            new()
            {
                JobName = "WorkIngestion",
                JobType = "Ingestion",
                Purpose = "Pull works/publications for configured institutions and upsert work/authorship tables.",
                RecommendedRunMode = "TestLoad",
                WritesData = true,
                SortOrder = 30
            },
            new()
            {
                JobName = "FacultyAuthorCandidateGeneration",
                JobType = "Matching",
                Purpose = "Generate faculty-author match candidates from faculty and OpenAlex author data.",
                RecommendedRunMode = "TestLoad",
                WritesData = true,
                SortOrder = 40
            },
            new()
            {
                JobName = "FacultyPublicationCandidateGeneration",
                JobType = "Matching",
                Purpose = "Generate faculty-publication candidates from accepted author decisions and work authorships.",
                RecommendedRunMode = "TestLoad",
                WritesData = true,
                SortOrder = 50
            },
            new()
            {
                JobName = "RunPipelineOnce",
                JobType = "Orchestration",
                Purpose = "Run the configured pipeline sequence once from the worker.",
                RecommendedRunMode = "TestLoad",
                WritesData = true,
                SortOrder = 60
            },
            new()
            {
                JobName = "InstitutionIngestionPreview",
                JobType = "Preview",
                Purpose = "Call OpenAlex institution endpoint and log result counts without writing institution rows.",
                RecommendedRunMode = "SmokeTest",
                WritesData = false,
                SortOrder = 70
            }
        }
        .OrderBy(job => job.SortOrder)
        .ToList();
    }

    private async Task<IReadOnlyList<RecentIngestionRunListItem>> GetRecentIngestionRunsAsync(
        int top,
        CancellationToken cancellationToken)
    {
        var rows = new List<RecentIngestionRunListItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.IngestionRun", "U", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 200)})
    IngestionRunId,
    RunMode,
    Status,
    InstitutionFilter,
    StartedAtUtc,
    FinishedAtUtc,
    ElapsedMs,
    StartedBy,
    MachineName,
    ApplicationVersion,
    ErrorCount,
    WarningCount
FROM openalex.IngestionRun
ORDER BY IngestionRunId DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RecentIngestionRunListItem
            {
                IngestionRunId = reader.GetInt64(0),
                RunMode = reader.IsDBNull(1) ? null : reader.GetString(1),
                Status = reader.IsDBNull(2) ? null : reader.GetString(2),
                InstitutionFilter = reader.IsDBNull(3) ? null : reader.GetString(3),
                StartedAtUtc = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                FinishedAtUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                ElapsedMs = reader.IsDBNull(6) ? null : Convert.ToInt64(reader.GetValue(6)),
                StartedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                MachineName = reader.IsDBNull(8) ? null : reader.GetString(8),
                ApplicationVersion = reader.IsDBNull(9) ? null : reader.GetString(9),
                ErrorCount = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                WarningCount = reader.IsDBNull(11) ? null : reader.GetInt32(11)
            });
        }

        return rows;
    }

    private static async Task<bool> ObjectExistsAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
