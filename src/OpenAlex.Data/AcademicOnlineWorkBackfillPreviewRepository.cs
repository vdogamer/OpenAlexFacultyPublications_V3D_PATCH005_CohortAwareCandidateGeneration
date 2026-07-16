using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcademicOnlineWorkBackfillPreviewRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcademicOnlineWorkBackfillPreviewRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long?> GetLatestAcademicOnlineComparisonRunIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AcademicOnlineComparisonRun", "U", cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT MAX(AcademicOnlineComparisonRunId)
FROM openalex.AcademicOnlineComparisonRun
WHERE RunStatus = 'Completed';";

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    public async Task<IReadOnlyList<AcademicOnlineWorkBackfillPreviewSourceRow>> LoadSourceRowsAsync(
        long comparisonRunId,
        long? facultyId,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AcademicOnlineWorkBackfillPreviewSourceRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AcademicOnlinePublicationSnapshot", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.AcademicOnlinePublicationSnapshot does not exist. Run PATCH016 first.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
;WITH SourceRows AS
(
    SELECT
        p.AcademicOnlinePublicationSnapshotId,
        p.AcademicOnlineComparisonRunId,
        p.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        s.AcademicOnlineName AS AcademicOnlineScholarName,
        p.AcademicOnlinePublicationId,
        p.Title,
        p.PublicationYear,
        p.Doi,
        p.OpenAlexId,
        NormalizedOpenAlexId = CASE
            WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NULL THEN NULL
            ELSE UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
        END,
        DuplicateKey = CASE
            WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NULL
                THEN 'SNAPSHOT:' + CONVERT(VARCHAR(30), p.AcademicOnlinePublicationSnapshotId)
            ELSE UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
        END,
        RowRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                p.FacultyId,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NULL
                        THEN 'SNAPSHOT:' + CONVERT(VARCHAR(30), p.AcademicOnlinePublicationSnapshotId)
                    ELSE UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                END
            ORDER BY p.IsVerifiedQuery DESC, p.Verified DESC, p.AcademicOnlinePublicationSnapshotId DESC
        ),
        SnapshotDuplicateCount = COUNT(*) OVER
        (
            PARTITION BY
                p.FacultyId,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NULL
                        THEN 'SNAPSHOT:' + CONVERT(VARCHAR(30), p.AcademicOnlinePublicationSnapshotId)
                    ELSE UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                END
        )
    FROM openalex.AcademicOnlinePublicationSnapshot p
    JOIN openalex.Faculty f
        ON p.FacultyId = f.FacultyId
    LEFT JOIN openalex.AcademicOnlineScholarSnapshot s
        ON s.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
       AND s.FacultyId = p.FacultyId
       AND (s.AcademicOnlineAuthorId = p.AcademicOnlineAuthorId OR s.AcademicOnlineAuthorId IS NULL)
    WHERE p.AcademicOnlineComparisonRunId = @ComparisonRunId
      AND (@FacultyId IS NULL OR p.FacultyId = @FacultyId)
)
SELECT TOP (@MaxRows)
    sr.AcademicOnlineComparisonRunId,
    sr.AcademicOnlinePublicationSnapshotId,
    sr.FacultyId,
    sr.FacultyDisplayName,
    sr.AcademicOnlineScholarName,
    sr.AcademicOnlinePublicationId,
    sr.Title,
    sr.PublicationYear,
    sr.Doi,
    sr.OpenAlexId,
    sr.NormalizedOpenAlexId,
    sr.SnapshotDuplicateCount,
    LocalWorkId = w.WorkId
FROM SourceRows sr
OUTER APPLY
(
    SELECT TOP (1) w.WorkId
    FROM openalex.Work w
    WHERE sr.NormalizedOpenAlexId IS NOT NULL
      AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(w.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) = sr.NormalizedOpenAlexId
    ORDER BY w.WorkId
) w
WHERE sr.RowRank = 1
  AND w.WorkId IS NULL
ORDER BY sr.PublicationYear DESC, sr.AcademicOnlinePublicationSnapshotId DESC;";
        command.Parameters.AddWithValue("@ComparisonRunId", comparisonRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId.HasValue ? facultyId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@MaxRows", Math.Clamp(maxRows, 1, 5000));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcademicOnlineWorkBackfillPreviewSourceRow
            {
                AcademicOnlineComparisonRunId = reader.GetInt64(reader.GetOrdinal("AcademicOnlineComparisonRunId")),
                AcademicOnlinePublicationSnapshotId = reader.GetInt64(reader.GetOrdinal("AcademicOnlinePublicationSnapshotId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                AcademicOnlineScholarName = GetNullableString(reader, "AcademicOnlineScholarName"),
                AcademicOnlinePublicationId = GetNullableString(reader, "AcademicOnlinePublicationId"),
                PublicationTitle = GetNullableString(reader, "Title"),
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                Doi = GetNullableString(reader, "Doi"),
                OpenAlexId = GetNullableString(reader, "OpenAlexId"),
                NormalizedOpenAlexId = GetNullableString(reader, "NormalizedOpenAlexId"),
                SnapshotDuplicateCount = reader.GetInt32(reader.GetOrdinal("SnapshotDuplicateCount")),
                LocalWorkId = GetNullableInt64(reader, "LocalWorkId")
            });
        }

        return rows;
    }

    public async Task<long> StartRunAsync(long comparisonRunId, long? facultyId, int maxWorks, string startedBy, string applicationVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsurePreviewTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlineWorkBackfillPreviewRun
(
    SourceAcademicOnlineComparisonRunId,
    FacultyId,
    MaxWorks,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AcademicOnlineWorkBackfillPreviewRunId
VALUES
(
    @ComparisonRunId,
    @FacultyId,
    @MaxWorks,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@ComparisonRunId", comparisonRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId.HasValue ? facultyId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@MaxWorks", maxWorks);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task SaveItemAsync(
        long previewRunId,
        AcademicOnlineWorkBackfillPreviewSourceRow source,
        string previewStatus,
        string? requestUriMasked,
        int? httpStatusCode,
        long? responseContentLength,
        string? returnedOpenAlexId,
        string? returnedTitle,
        string? returnedDoi,
        int? returnedPublicationYear,
        string? returnedWorkType,
        int? returnedCitedByCount,
        string? rawResponseJson,
        string? errorText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlineWorkBackfillPreviewItem
(
    AcademicOnlineWorkBackfillPreviewRunId,
    SourceAcademicOnlineComparisonRunId,
    AcademicOnlinePublicationSnapshotId,
    FacultyId,
    FacultyDisplayName,
    AcademicOnlineScholarName,
    AcademicOnlinePublicationId,
    AcademicOnlinePublicationTitle,
    AcademicOnlinePublicationYear,
    AcademicOnlineDoi,
    AcademicOnlineOpenAlexId,
    NormalizedOpenAlexId,
    SnapshotDuplicateCount,
    LocalWorkIdAtSelection,
    PreviewStatus,
    OpenAlexRequestUriMasked,
    OpenAlexHttpStatusCode,
    OpenAlexResponseContentLength,
    ReturnedOpenAlexId,
    ReturnedTitle,
    ReturnedDoi,
    ReturnedPublicationYear,
    ReturnedWorkType,
    ReturnedCitedByCount,
    RawResponseJson,
    ErrorText,
    CreatedAtUtc
)
VALUES
(
    @PreviewRunId,
    @ComparisonRunId,
    @SnapshotId,
    @FacultyId,
    @FacultyDisplayName,
    @ScholarName,
    @PublicationId,
    @PublicationTitle,
    @PublicationYear,
    @Doi,
    @OpenAlexId,
    @NormalizedOpenAlexId,
    @DuplicateCount,
    @LocalWorkId,
    @PreviewStatus,
    @RequestUri,
    @HttpStatusCode,
    @ResponseLength,
    @ReturnedOpenAlexId,
    @ReturnedTitle,
    @ReturnedDoi,
    @ReturnedPublicationYear,
    @ReturnedWorkType,
    @ReturnedCitedByCount,
    @RawResponseJson,
    @ErrorText,
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@ComparisonRunId", source.AcademicOnlineComparisonRunId);
        command.Parameters.AddWithValue("@SnapshotId", source.AcademicOnlinePublicationSnapshotId);
        command.Parameters.AddWithValue("@FacultyId", source.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", source.FacultyDisplayName);
        AddNullable(command, "@ScholarName", SqlDbType.NVarChar, 512, source.AcademicOnlineScholarName);
        AddNullable(command, "@PublicationId", SqlDbType.NVarChar, 128, source.AcademicOnlinePublicationId);
        AddNullable(command, "@PublicationTitle", SqlDbType.NVarChar, -1, source.PublicationTitle);
        AddNullable(command, "@PublicationYear", SqlDbType.Int, source.PublicationYear);
        AddNullable(command, "@Doi", SqlDbType.NVarChar, 512, source.Doi);
        AddNullable(command, "@OpenAlexId", SqlDbType.NVarChar, 128, source.OpenAlexId);
        AddNullable(command, "@NormalizedOpenAlexId", SqlDbType.NVarChar, 128, source.NormalizedOpenAlexId);
        command.Parameters.AddWithValue("@DuplicateCount", source.SnapshotDuplicateCount);
        AddNullable(command, "@LocalWorkId", SqlDbType.BigInt, source.LocalWorkId);
        command.Parameters.AddWithValue("@PreviewStatus", previewStatus);
        AddNullable(command, "@RequestUri", SqlDbType.NVarChar, 2048, requestUriMasked);
        AddNullable(command, "@HttpStatusCode", SqlDbType.Int, httpStatusCode);
        AddNullable(command, "@ResponseLength", SqlDbType.BigInt, responseContentLength);
        AddNullable(command, "@ReturnedOpenAlexId", SqlDbType.NVarChar, 128, returnedOpenAlexId);
        AddNullable(command, "@ReturnedTitle", SqlDbType.NVarChar, -1, returnedTitle);
        AddNullable(command, "@ReturnedDoi", SqlDbType.NVarChar, 512, returnedDoi);
        AddNullable(command, "@ReturnedPublicationYear", SqlDbType.Int, returnedPublicationYear);
        AddNullable(command, "@ReturnedWorkType", SqlDbType.NVarChar, 100, returnedWorkType);
        AddNullable(command, "@ReturnedCitedByCount", SqlDbType.Int, returnedCitedByCount);
        AddNullable(command, "@RawResponseJson", SqlDbType.NVarChar, -1, rawResponseJson);
        AddNullable(command, "@ErrorText", SqlDbType.NVarChar, -1, errorText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(long previewRunId, int selected, int ready, int exists, int missingId, int notFound, int failed, int duplicatesSkipped, string summary, CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(previewRunId, "Completed", selected, ready, exists, missingId, notFound, failed, duplicatesSkipped, summary, null, cancellationToken);
    }

    public async Task FailRunAsync(long previewRunId, Exception exception, CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(previewRunId, "Failed", 0, 0, 0, 0, 0, 0, 0, null, exception.ToString(), cancellationToken);
    }

    private async Task UpdateRunAsync(long previewRunId, string status, int selected, int ready, int exists, int missingId, int notFound, int failed, int duplicatesSkipped, string? summary, string? error, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcademicOnlineWorkBackfillPreviewRun
SET
    SelectedWorkCount = @Selected,
    ReadyToFetchCount = @Ready,
    AlreadyExistsLocallyCount = @Exists,
    MissingOpenAlexIdCount = @MissingId,
    NotFoundByOpenAlexCount = @NotFound,
    FetchFailedCount = @Failed,
    DuplicateSnapshotRecordsSkipped = @DuplicatesSkipped,
    RunStatus = @Status,
    SummaryText = @Summary,
    ErrorText = @Error,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId;";
        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@Selected", selected);
        command.Parameters.AddWithValue("@Ready", ready);
        command.Parameters.AddWithValue("@Exists", exists);
        command.Parameters.AddWithValue("@MissingId", missingId);
        command.Parameters.AddWithValue("@NotFound", notFound);
        command.Parameters.AddWithValue("@Failed", failed);
        command.Parameters.AddWithValue("@DuplicatesSkipped", duplicatesSkipped);
        command.Parameters.AddWithValue("@Status", status);
        AddNullable(command, "@Summary", SqlDbType.NVarChar, -1, summary);
        AddNullable(command, "@Error", SqlDbType.NVarChar, -1, error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePreviewTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(connection, "openalex.AcademicOnlineWorkBackfillPreviewRun", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.AcademicOnlineWorkBackfillPreviewItem", "U", cancellationToken))
        {
            throw new InvalidOperationException("PATCH017B preview tables do not exist. Run sql/370_V3E_PATCH017B_AcademicOnlineGuidedWorkBackfillPreview_SQL2016.sql first.");
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

    private static long? GetNullableInt64(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }
}
