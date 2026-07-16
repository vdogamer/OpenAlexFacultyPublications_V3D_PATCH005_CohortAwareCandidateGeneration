using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcademicOnlineWorkBackfillRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcademicOnlineWorkBackfillRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AcademicOnlineWorkBackfillExecutionSourceRow>> LoadReadyItemsAsync(
        long previewRunId,
        int maxWorks,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AcademicOnlineWorkBackfillExecutionSourceRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureExecutionTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@MaxWorks)
    i.AcademicOnlineWorkBackfillPreviewItemId,
    i.AcademicOnlineWorkBackfillPreviewRunId,
    i.FacultyId,
    i.FacultyDisplayName,
    i.AcademicOnlinePublicationId,
    i.AcademicOnlinePublicationTitle,
    i.AcademicOnlinePublicationYear,
    i.NormalizedOpenAlexId,
    i.ReturnedOpenAlexId,
    i.ReturnedTitle,
    i.RawResponseJson
FROM openalex.AcademicOnlineWorkBackfillPreviewItem i
JOIN openalex.AcademicOnlineWorkBackfillPreviewRun r
    ON r.AcademicOnlineWorkBackfillPreviewRunId = i.AcademicOnlineWorkBackfillPreviewRunId
WHERE i.AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId
  AND r.RunStatus = 'Completed'
  AND i.PreviewStatus = 'ReadyToFetch'
ORDER BY i.AcademicOnlineWorkBackfillPreviewItemId;";
        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@MaxWorks", Math.Clamp(maxWorks, 1, 5));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcademicOnlineWorkBackfillExecutionSourceRow
            {
                AcademicOnlineWorkBackfillPreviewItemId = reader.GetInt64(reader.GetOrdinal("AcademicOnlineWorkBackfillPreviewItemId")),
                AcademicOnlineWorkBackfillPreviewRunId = reader.GetInt64(reader.GetOrdinal("AcademicOnlineWorkBackfillPreviewRunId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                AcademicOnlinePublicationId = GetNullableString(reader, "AcademicOnlinePublicationId"),
                AcademicOnlinePublicationTitle = GetNullableString(reader, "AcademicOnlinePublicationTitle"),
                AcademicOnlinePublicationYear = GetNullableInt32(reader, "AcademicOnlinePublicationYear"),
                NormalizedOpenAlexId = GetNullableString(reader, "NormalizedOpenAlexId"),
                ReturnedOpenAlexId = GetNullableString(reader, "ReturnedOpenAlexId"),
                ReturnedTitle = GetNullableString(reader, "ReturnedTitle"),
                RawResponseJson = GetNullableString(reader, "RawResponseJson")
            });
        }

        return rows;
    }

    public async Task<BackfillSafetyCounts> GetSafetyCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var candidateRows = await CountRowsAsync(connection, "openalex.FacultyPublicationCandidate", cancellationToken);
        var decisionRows = await CountRowsAsync(connection, "openalex.FacultyPublicationDecision", cancellationToken);

        return new BackfillSafetyCounts
        {
            CandidateRows = candidateRows,
            DecisionRows = decisionRows
        };
    }

    public async Task<long> StartRunAsync(
        long sourcePreviewRunId,
        int maxWorks,
        BackfillSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureExecutionTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlineWorkBackfillRun
(
    SourcePreviewRunId,
    MaxWorks,
    CandidateRowsBefore,
    DecisionRowsBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AcademicOnlineWorkBackfillRunId
VALUES
(
    @SourcePreviewRunId,
    @MaxWorks,
    @CandidateRowsBefore,
    @DecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@SourcePreviewRunId", sourcePreviewRunId);
        command.Parameters.AddWithValue("@MaxWorks", Math.Clamp(maxWorks, 1, 5));
        command.Parameters.AddWithValue("@CandidateRowsBefore", safetyBefore.CandidateRows);
        command.Parameters.AddWithValue("@DecisionRowsBefore", safetyBefore.DecisionRows);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task AttachWorkIngestionRunAsync(
        long backfillRunId,
        long workIngestionRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcademicOnlineWorkBackfillRun
SET WorkIngestionRunId = @WorkIngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineWorkBackfillRunId = @BackfillRunId;";
        command.Parameters.AddWithValue("@BackfillRunId", backfillRunId);
        command.Parameters.AddWithValue("@WorkIngestionRunId", workIngestionRunId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> GetLocalWorkIdAsync(string normalizedOpenAlexId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (1) WorkId
FROM openalex.Work
WHERE UPPER(REPLACE(REPLACE(LTRIM(RTRIM(OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) = @NormalizedOpenAlexId
ORDER BY WorkId;";
        command.Parameters.AddWithValue("@NormalizedOpenAlexId", normalizedOpenAlexId.Trim().ToUpperInvariant());

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    public async Task SaveItemAsync(
        long backfillRunId,
        AcademicOnlineWorkBackfillExecutionSourceRow source,
        string executionStatus,
        long? localWorkIdBefore,
        long? workIdAfter,
        bool workInserted,
        int authorshipRows,
        int authorshipInstitutionRows,
        int matchedInstitutionRows,
        string? errorText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlineWorkBackfillItem
(
    AcademicOnlineWorkBackfillRunId,
    SourcePreviewItemId,
    SourcePreviewRunId,
    FacultyId,
    FacultyDisplayName,
    AcademicOnlinePublicationId,
    AcademicOnlinePublicationTitle,
    NormalizedOpenAlexId,
    LocalWorkIdBefore,
    WorkIdAfter,
    WorkInserted,
    ExecutionStatus,
    AuthorshipRows,
    AuthorshipInstitutionRows,
    MatchedInstitutionRows,
    ErrorText,
    CreatedAtUtc
)
VALUES
(
    @BackfillRunId,
    @SourcePreviewItemId,
    @SourcePreviewRunId,
    @FacultyId,
    @FacultyDisplayName,
    @AcademicOnlinePublicationId,
    @AcademicOnlinePublicationTitle,
    @NormalizedOpenAlexId,
    @LocalWorkIdBefore,
    @WorkIdAfter,
    @WorkInserted,
    @ExecutionStatus,
    @AuthorshipRows,
    @AuthorshipInstitutionRows,
    @MatchedInstitutionRows,
    @ErrorText,
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@BackfillRunId", backfillRunId);
        command.Parameters.AddWithValue("@SourcePreviewItemId", source.AcademicOnlineWorkBackfillPreviewItemId);
        command.Parameters.AddWithValue("@SourcePreviewRunId", source.AcademicOnlineWorkBackfillPreviewRunId);
        command.Parameters.AddWithValue("@FacultyId", source.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", source.FacultyDisplayName);
        AddNullable(command, "@AcademicOnlinePublicationId", SqlDbType.NVarChar, 128, source.AcademicOnlinePublicationId);
        AddNullable(command, "@AcademicOnlinePublicationTitle", SqlDbType.NVarChar, -1, source.AcademicOnlinePublicationTitle);
        AddNullable(command, "@NormalizedOpenAlexId", SqlDbType.NVarChar, 128, source.NormalizedOpenAlexId);
        AddNullable(command, "@LocalWorkIdBefore", SqlDbType.BigInt, localWorkIdBefore);
        AddNullable(command, "@WorkIdAfter", SqlDbType.BigInt, workIdAfter);
        command.Parameters.AddWithValue("@WorkInserted", workInserted);
        command.Parameters.AddWithValue("@ExecutionStatus", executionStatus);
        command.Parameters.AddWithValue("@AuthorshipRows", authorshipRows);
        command.Parameters.AddWithValue("@AuthorshipInstitutionRows", authorshipInstitutionRows);
        command.Parameters.AddWithValue("@MatchedInstitutionRows", matchedInstitutionRows);
        AddNullable(command, "@ErrorText", SqlDbType.NVarChar, -1, errorText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        long backfillRunId,
        int selected,
        int inserted,
        int alreadyExists,
        int invalid,
        int failed,
        int authorshipRows,
        int authorshipInstitutionRows,
        int matchedInstitutionRows,
        BackfillSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcademicOnlineWorkBackfillRun
SET SelectedWorkCount = @Selected,
    InsertedWorkCount = @Inserted,
    AlreadyExistsCount = @AlreadyExists,
    InvalidPreviewItemCount = @Invalid,
    FailedItemCount = @Failed,
    AuthorshipRows = @AuthorshipRows,
    AuthorshipInstitutionRows = @AuthorshipInstitutionRows,
    MatchedInstitutionRows = @MatchedInstitutionRows,
    CandidateRowsAfter = @CandidateRowsAfter,
    DecisionRowsAfter = @DecisionRowsAfter,
    RunStatus = CASE WHEN @Failed > 0 OR @Invalid > 0 THEN 'CompletedWithWarnings' ELSE 'Completed' END,
    SummaryText = @Summary,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineWorkBackfillRunId = @BackfillRunId;";
        command.Parameters.AddWithValue("@BackfillRunId", backfillRunId);
        command.Parameters.AddWithValue("@Selected", selected);
        command.Parameters.AddWithValue("@Inserted", inserted);
        command.Parameters.AddWithValue("@AlreadyExists", alreadyExists);
        command.Parameters.AddWithValue("@Invalid", invalid);
        command.Parameters.AddWithValue("@Failed", failed);
        command.Parameters.AddWithValue("@AuthorshipRows", authorshipRows);
        command.Parameters.AddWithValue("@AuthorshipInstitutionRows", authorshipInstitutionRows);
        command.Parameters.AddWithValue("@MatchedInstitutionRows", matchedInstitutionRows);
        command.Parameters.AddWithValue("@CandidateRowsAfter", safetyAfter.CandidateRows);
        command.Parameters.AddWithValue("@DecisionRowsAfter", safetyAfter.DecisionRows);
        command.Parameters.AddWithValue("@Summary", summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRunAsync(long backfillRunId, Exception exception, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcademicOnlineWorkBackfillRun
SET RunStatus = 'Failed',
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineWorkBackfillRunId = @BackfillRunId;";
        command.Parameters.AddWithValue("@BackfillRunId", backfillRunId);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountRowsAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, tableName, "U", cancellationToken))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + tableName + ";";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureExecutionTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, "openalex.AcademicOnlineWorkBackfillRun", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.AcademicOnlineWorkBackfillItem", "U", cancellationToken))
        {
            throw new InvalidOperationException("PATCH017B1 execution tables are missing. Run sql/372_V3E_PATCH017B1_ControlledAcademicOnlineGuidedWorkBackfill_SQL2016.sql first.");
        }
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, long? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
