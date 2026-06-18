using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AuthorIngestionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AuthorIngestionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IngestionRunStartResult> StartRunAsync(
        string runMode,
        string institutionFilter,
        int? maxPages,
        int? maxRecords,
        string applicationVersion,
        string startedBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @Inserted TABLE
(
    IngestionRunId BIGINT,
    RunCorrelationId UNIQUEIDENTIFIER
);

INSERT INTO openalex.IngestionRun
(
    RunMode,
    Status,
    InstitutionFilter,
    ClearBeforeRun,
    MaxPagesPerEntity,
    MaxRecordsPerEntity,
    StartedBy,
    MachineName,
    ApplicationVersion,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.IngestionRunId, INSERTED.RunCorrelationId
INTO @Inserted
VALUES
(
    @RunMode,
    'Started',
    @InstitutionFilter,
    0,
    @MaxPages,
    @MaxRecords,
    @StartedBy,
    @MachineName,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

SELECT IngestionRunId, RunCorrelationId FROM @Inserted;";

        command.Parameters.AddWithValue("@RunMode", runMode);
        command.Parameters.AddWithValue("@InstitutionFilter", institutionFilter);
        command.Parameters.AddWithValue("@MaxPages", (object?)maxPages ?? DBNull.Value);
        command.Parameters.AddWithValue("@MaxRecords", (object?)maxRecords ?? DBNull.Value);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@MachineName", Environment.MachineName);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Failed to create openalex.IngestionRun row.");
        }

        return new IngestionRunStartResult
        {
            IngestionRunId = reader.GetInt64(0),
            RunCorrelationId = reader.GetGuid(1)
        };
    }

    public async Task<long> StartRequestLogAsync(
        long ingestionRunId,
        string requestUrl,
        string requestUrlMasked,
        string filterText,
        string cursorValue,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.RequestLog
(
    IngestionRunId,
    EntityType,
    RequestMethod,
    RequestUrl,
    RequestUrlMasked,
    FilterText,
    CursorValue,
    PageNumberWithinRun,
    AttemptNumber,
    MaxAttempts,
    RequestSucceeded,
    StartedAtUtc,
    CreatedAtUtc
)
OUTPUT INSERTED.RequestLogId
VALUES
(
    @IngestionRunId,
    'Author',
    'GET',
    @RequestUrl,
    @RequestUrlMasked,
    @FilterText,
    @CursorValue,
    @PageNumberWithinRun,
    1,
    1,
    0,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@RequestUrl", requestUrl);
        command.Parameters.AddWithValue("@RequestUrlMasked", requestUrlMasked);
        command.Parameters.AddWithValue("@FilterText", filterText);
        command.Parameters.AddWithValue("@CursorValue", cursorValue);
        command.Parameters.AddWithValue("@PageNumberWithinRun", pageNumber);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task CompleteRequestLogAsync(
        long requestLogId,
        int httpStatusCode,
        int resultCount,
        int? metaCount,
        int? metaDbResponseTimeMs,
        int? metaPage,
        int? metaPerPage,
        string? metaNextCursor,
        long? responseContentLength,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @FinishedAtUtc DATETIME2(7) = SYSUTCDATETIME();

UPDATE openalex.RequestLog
SET
    HttpStatusCode = @HttpStatusCode,
    RequestSucceeded = 1,
    FinishedAtUtc = @FinishedAtUtc,
    ElapsedMs = DATEDIFF_BIG(MILLISECOND, StartedAtUtc, @FinishedAtUtc),
    MetaCount = @MetaCount,
    MetaDbResponseTimeMs = @MetaDbResponseTimeMs,
    MetaPage = @MetaPage,
    MetaPerPage = @MetaPerPage,
    MetaNextCursor = @MetaNextCursor,
    ResultCount = @ResultCount,
    ResponseContentLength = @ResponseContentLength
WHERE RequestLogId = @RequestLogId;";

        command.Parameters.AddWithValue("@RequestLogId", requestLogId);
        command.Parameters.AddWithValue("@HttpStatusCode", httpStatusCode);
        command.Parameters.AddWithValue("@ResultCount", resultCount);
        command.Parameters.AddWithValue("@MetaCount", (object?)metaCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@MetaDbResponseTimeMs", (object?)metaDbResponseTimeMs ?? DBNull.Value);
        command.Parameters.AddWithValue("@MetaPage", (object?)metaPage ?? DBNull.Value);
        command.Parameters.AddWithValue("@MetaPerPage", (object?)metaPerPage ?? DBNull.Value);
        command.Parameters.AddWithValue("@MetaNextCursor", (object?)metaNextCursor ?? DBNull.Value);
        command.Parameters.AddWithValue("@ResponseContentLength", (object?)responseContentLength ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRequestLogAsync(long requestLogId, Exception exception, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @FinishedAtUtc DATETIME2(7) = SYSUTCDATETIME();

UPDATE openalex.RequestLog
SET
    RequestSucceeded = 0,
    FinishedAtUtc = @FinishedAtUtc,
    ElapsedMs = DATEDIFF_BIG(MILLISECOND, StartedAtUtc, @FinishedAtUtc),
    ExceptionType = @ExceptionType,
    ErrorMessage = @ErrorMessage,
    ErrorResponseSnippet = @ErrorResponseSnippet
WHERE RequestLogId = @RequestLogId;";

        command.Parameters.AddWithValue("@RequestLogId", requestLogId);
        command.Parameters.AddWithValue("@ExceptionType", exception.GetType().FullName ?? exception.GetType().Name);
        command.Parameters.AddWithValue("@ErrorMessage", exception.Message);
        command.Parameters.AddWithValue("@ErrorResponseSnippet", exception.ToString()[..Math.Min(4000, exception.ToString().Length)]);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthorUpsertResult> UpsertAuthorAsync(
        JsonElement author,
        long ingestionRunId,
        CancellationToken cancellationToken = default)
    {
        var rawJson = author.GetRawText();
        var openAlexId = GetString(author, "id");

        if (string.IsNullOrWhiteSpace(openAlexId))
        {
            throw new InvalidOperationException("OpenAlex author row is missing required id.");
        }

        var ids = GetObject(author, "ids");
        var summaryStats = GetObject(author, "summary_stats");

        var openAlexShortId = GetOpenAlexShortId(openAlexId);
        var orcid = GetString(author, "orcid") ?? (ids is null ? null : GetString(ids.Value, "orcid"));
        var displayName = GetString(author, "display_name");
        var worksCount = GetNullableInt(author, "works_count");
        var citedByCount = GetNullableInt(author, "cited_by_count");

        var twoYearMeanCitedness = summaryStats is null ? null : GetNullableDecimal(summaryStats.Value, "2yr_mean_citedness");
        var hIndex = summaryStats is null ? null : GetNullableInt(summaryStats.Value, "h_index");
        var i10Index = summaryStats is null ? null : GetNullableInt(summaryStats.Value, "i10_index");

        var createdDate = GetNullableDate(author, "created_date");
        var updatedDate = GetNullableDate(author, "updated_date");
        var rawJsonHash = Sha256Hex(rawJson);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingAuthorId = await GetExistingAuthorIdAsync(connection, transaction, openAlexId, cancellationToken);

            bool inserted = existingAuthorId is null;
            bool updated = existingAuthorId is not null;
            long authorId;

            if (inserted)
            {
                authorId = await InsertAuthorAsync(
                    connection,
                    transaction,
                    ingestionRunId,
                    openAlexId,
                    openAlexShortId,
                    orcid,
                    displayName,
                    worksCount,
                    citedByCount,
                    twoYearMeanCitedness,
                    hIndex,
                    i10Index,
                    createdDate,
                    updatedDate,
                    rawJson,
                    rawJsonHash,
                    cancellationToken);
            }
            else
            {
                authorId = existingAuthorId.Value;

                await UpdateAuthorAsync(
                    connection,
                    transaction,
                    authorId,
                    ingestionRunId,
                    openAlexShortId,
                    orcid,
                    displayName,
                    worksCount,
                    citedByCount,
                    twoYearMeanCitedness,
                    hIndex,
                    i10Index,
                    createdDate,
                    updatedDate,
                    rawJson,
                    rawJsonHash,
                    cancellationToken);
            }

            await ReplaceAuthorLastKnownInstitutionsAsync(
                connection,
                transaction,
                authorId,
                openAlexId,
                author,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new AuthorUpsertResult
            {
                AuthorId = authorId,
                OpenAlexId = openAlexId,
                Inserted = inserted,
                Updated = updated
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CompleteRunAsync(
        long ingestionRunId,
        int processed,
        int inserted,
        int updated,
        int totalRequests,
        int successfulRequests,
        int failedRequests,
        string summaryText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @FinishedAtUtc DATETIME2(7) = SYSUTCDATETIME();

UPDATE openalex.IngestionRun
SET
    Status = CASE WHEN @FailedRequests > 0 THEN 'CompletedWithWarnings' ELSE 'Completed' END,
    FinishedAtUtc = @FinishedAtUtc,
    ElapsedMs = DATEDIFF_BIG(MILLISECOND, StartedAtUtc, @FinishedAtUtc),
    WarningCount = @FailedRequests,
    UpdatedAtUtc = @FinishedAtUtc
WHERE IngestionRunId = @IngestionRunId;

INSERT INTO openalex.IngestionRunSummary
(
    IngestionRunId,
    TotalRequests,
    SuccessfulRequests,
    FailedRequests,
    TotalRetries,
    AuthorsProcessed,
    AuthorsInserted,
    AuthorsUpdated,
    SummaryText,
    CreatedAtUtc
)
VALUES
(
    @IngestionRunId,
    @TotalRequests,
    @SuccessfulRequests,
    @FailedRequests,
    0,
    @Processed,
    @Inserted,
    @Updated,
    @SummaryText,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@Processed", processed);
        command.Parameters.AddWithValue("@Inserted", inserted);
        command.Parameters.AddWithValue("@Updated", updated);
        command.Parameters.AddWithValue("@TotalRequests", totalRequests);
        command.Parameters.AddWithValue("@SuccessfulRequests", successfulRequests);
        command.Parameters.AddWithValue("@FailedRequests", failedRequests);
        command.Parameters.AddWithValue("@SummaryText", summaryText);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRunAsync(long ingestionRunId, Exception exception, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @FinishedAtUtc DATETIME2(7) = SYSUTCDATETIME();

UPDATE openalex.IngestionRun
SET
    Status = 'Failed',
    FinishedAtUtc = @FinishedAtUtc,
    ElapsedMs = DATEDIFF_BIG(MILLISECOND, StartedAtUtc, @FinishedAtUtc),
    ErrorCount = ErrorCount + 1,
    UpdatedAtUtc = @FinishedAtUtc
WHERE IngestionRunId = @IngestionRunId;

INSERT INTO openalex.IngestionRunSummary
(
    IngestionRunId,
    FailedRequests,
    SummaryText,
    CreatedAtUtc
)
VALUES
(
    @IngestionRunId,
    1,
    @SummaryText,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@SummaryText", exception.Message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> GetExistingAuthorIdAsync(SqlConnection connection, SqlTransaction transaction, string openAlexId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT AuthorId FROM openalex.Author WHERE OpenAlexId = @OpenAlexId;";
        command.Parameters.AddWithValue("@OpenAlexId", openAlexId);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static async Task<long> InsertAuthorAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long ingestionRunId,
        string openAlexId,
        string? openAlexShortId,
        string? orcid,
        string? displayName,
        int? worksCount,
        int? citedByCount,
        decimal? twoYearMeanCitedness,
        int? hIndex,
        int? i10Index,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO openalex.Author
(
    OpenAlexId,
    OpenAlexShortId,
    Orcid,
    DisplayName,
    WorksCount,
    CitedByCount,
    SummaryStats2YrMeanCitedness,
    SummaryStatsHIndex,
    SummaryStatsI10Index,
    CreatedDate,
    UpdatedDate,
    RawJson,
    RawJsonHash,
    CreatedAtUtc,
    UpdatedAtUtc,
    LastSeenAtUtc,
    LastIngestionRunId
)
OUTPUT INSERTED.AuthorId
VALUES
(
    @OpenAlexId,
    @OpenAlexShortId,
    @Orcid,
    @DisplayName,
    @WorksCount,
    @CitedByCount,
    @SummaryStats2YrMeanCitedness,
    @SummaryStatsHIndex,
    @SummaryStatsI10Index,
    @CreatedDate,
    @UpdatedDate,
    @RawJson,
    @RawJsonHash,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    @LastIngestionRunId
);";

        AddAuthorParameters(command, ingestionRunId, openAlexId, openAlexShortId, orcid, displayName, worksCount, citedByCount, twoYearMeanCitedness, hIndex, i10Index, createdDate, updatedDate, rawJson, rawJsonHash);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpdateAuthorAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long authorId,
        long ingestionRunId,
        string? openAlexShortId,
        string? orcid,
        string? displayName,
        int? worksCount,
        int? citedByCount,
        decimal? twoYearMeanCitedness,
        int? hIndex,
        int? i10Index,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE openalex.Author
SET
    OpenAlexShortId = @OpenAlexShortId,
    Orcid = @Orcid,
    DisplayName = @DisplayName,
    WorksCount = @WorksCount,
    CitedByCount = @CitedByCount,
    SummaryStats2YrMeanCitedness = @SummaryStats2YrMeanCitedness,
    SummaryStatsHIndex = @SummaryStatsHIndex,
    SummaryStatsI10Index = @SummaryStatsI10Index,
    CreatedDate = @CreatedDate,
    UpdatedDate = @UpdatedDate,
    RawJson = @RawJson,
    RawJsonHash = @RawJsonHash,
    UpdatedAtUtc = SYSUTCDATETIME(),
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastIngestionRunId = @LastIngestionRunId
WHERE AuthorId = @AuthorId;";

        command.Parameters.AddWithValue("@AuthorId", authorId);
        AddAuthorParameters(command, ingestionRunId, null, openAlexShortId, orcid, displayName, worksCount, citedByCount, twoYearMeanCitedness, hIndex, i10Index, createdDate, updatedDate, rawJson, rawJsonHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddAuthorParameters(
        SqlCommand command,
        long ingestionRunId,
        string? openAlexId,
        string? openAlexShortId,
        string? orcid,
        string? displayName,
        int? worksCount,
        int? citedByCount,
        decimal? twoYearMeanCitedness,
        int? hIndex,
        int? i10Index,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash)
    {
        if (openAlexId is not null)
        {
            command.Parameters.AddWithValue("@OpenAlexId", openAlexId);
        }

        command.Parameters.AddWithValue("@OpenAlexShortId", (object?)openAlexShortId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Orcid", (object?)orcid ?? DBNull.Value);
        command.Parameters.AddWithValue("@DisplayName", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("@WorksCount", (object?)worksCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@CitedByCount", (object?)citedByCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@SummaryStats2YrMeanCitedness", (object?)twoYearMeanCitedness ?? DBNull.Value);
        command.Parameters.AddWithValue("@SummaryStatsHIndex", (object?)hIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("@SummaryStatsI10Index", (object?)i10Index ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedDate", (object?)createdDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@UpdatedDate", (object?)updatedDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@RawJson", rawJson);
        command.Parameters.AddWithValue("@RawJsonHash", rawJsonHash);
        command.Parameters.AddWithValue("@LastIngestionRunId", ingestionRunId);
    }

    private static async Task ReplaceAuthorLastKnownInstitutionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long authorId,
        string authorOpenAlexId,
        JsonElement author,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM openalex.AuthorLastKnownInstitution WHERE AuthorId = @AuthorId;";
            delete.Parameters.AddWithValue("@AuthorId", authorId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var institutions = EnumerateLastKnownInstitutions(author).ToList();

        for (var i = 0; i < institutions.Count; i++)
        {
            var institution = institutions[i];
            var ids = GetObject(institution, "ids");
            var geo = GetObject(institution, "geo");

            var institutionOpenAlexId = GetString(institution, "id");
            var institutionDisplayName = GetString(institution, "display_name");
            var institutionRor = GetString(institution, "ror") ?? (ids is null ? null : GetString(ids.Value, "ror"));
            var countryCode = GetString(institution, "country_code") ?? (geo is null ? null : GetString(geo.Value, "country_code"));
            var institutionType = GetString(institution, "type");
            var rawJson = institution.GetRawText();

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
INSERT INTO openalex.AuthorLastKnownInstitution
(
    AuthorId,
    AuthorOpenAlexId,
    InstitutionOpenAlexId,
    InstitutionDisplayName,
    InstitutionRor,
    CountryCode,
    InstitutionType,
    OrdinalPosition,
    RawJson,
    CreatedAtUtc
)
VALUES
(
    @AuthorId,
    @AuthorOpenAlexId,
    @InstitutionOpenAlexId,
    @InstitutionDisplayName,
    @InstitutionRor,
    @CountryCode,
    @InstitutionType,
    @OrdinalPosition,
    @RawJson,
    SYSUTCDATETIME()
);";

            insert.Parameters.AddWithValue("@AuthorId", authorId);
            insert.Parameters.AddWithValue("@AuthorOpenAlexId", authorOpenAlexId);
            insert.Parameters.AddWithValue("@InstitutionOpenAlexId", (object?)institutionOpenAlexId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@InstitutionDisplayName", (object?)institutionDisplayName ?? DBNull.Value);
            insert.Parameters.AddWithValue("@InstitutionRor", (object?)institutionRor ?? DBNull.Value);
            insert.Parameters.AddWithValue("@CountryCode", (object?)countryCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("@InstitutionType", (object?)institutionType ?? DBNull.Value);
            insert.Parameters.AddWithValue("@OrdinalPosition", i);
            insert.Parameters.AddWithValue("@RawJson", rawJson);

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<JsonElement> EnumerateLastKnownInstitutions(JsonElement author)
    {
        if (author.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (author.TryGetProperty("last_known_institutions", out var institutions) &&
            institutions.ValueKind == JsonValueKind.Array)
        {
            foreach (var institution in institutions.EnumerateArray())
            {
                if (institution.ValueKind == JsonValueKind.Object)
                {
                    yield return institution;
                }
            }

            yield break;
        }

        if (author.TryGetProperty("last_known_institution", out var institutionObject) &&
            institutionObject.ValueKind == JsonValueKind.Object)
        {
            yield return institutionObject;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object ? value : null;
    }

    private static int? GetNullableInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? GetNullableDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? GetNullableDate(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTime.TryParse(value, out var parsed) ? parsed.Date : null;
    }

    private static string? GetOpenAlexShortId(string? openAlexId)
    {
        if (string.IsNullOrWhiteSpace(openAlexId))
        {
            return null;
        }

        var slashIndex = openAlexId.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < openAlexId.Length - 1
            ? openAlexId[(slashIndex + 1)..]
            : openAlexId;
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
