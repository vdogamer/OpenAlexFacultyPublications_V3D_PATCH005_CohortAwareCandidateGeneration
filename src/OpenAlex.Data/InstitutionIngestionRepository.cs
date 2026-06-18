using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class InstitutionIngestionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public InstitutionIngestionRepository(SqlConnectionFactory connectionFactory)
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
    'Institution',
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
    ErrorMessage = @ErrorMessage
WHERE RequestLogId = @RequestLogId;";

        command.Parameters.AddWithValue("@RequestLogId", requestLogId);
        command.Parameters.AddWithValue("@ExceptionType", exception.GetType().FullName ?? exception.GetType().Name);
        command.Parameters.AddWithValue("@ErrorMessage", exception.Message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InstitutionUpsertResult> UpsertInstitutionAsync(
        JsonElement institution,
        long ingestionRunId,
        CancellationToken cancellationToken = default)
    {
        var rawJson = institution.GetRawText();
        var openAlexId = GetString(institution, "id");

        if (string.IsNullOrWhiteSpace(openAlexId))
        {
            throw new InvalidOperationException("OpenAlex institution row is missing required id.");
        }

        var openAlexShortId = GetOpenAlexShortId(openAlexId);
        var ids = institution.TryGetProperty("ids", out var idsElement) && idsElement.ValueKind == JsonValueKind.Object
            ? idsElement
            : default;

        var ror = GetString(ids, "ror") ?? GetString(institution, "ror");
        var displayName = GetString(institution, "display_name");
        var countryCode = GetString(institution, "country_code");
        var continent = GetString(institution, "continent") ?? GetNestedString(institution, "geo", "continent");
        var type = GetString(institution, "type");
        var isGlobalSouth = GetNullableBool(institution, "is_global_south");
        var isSuperSystem = GetNullableBool(institution, "is_super_system");
        var worksCount = GetNullableInt(institution, "works_count");
        var citedByCount = GetNullableInt(institution, "cited_by_count");
        var homepageUrl = GetString(institution, "homepage_url");
        var imageUrl = GetString(institution, "image_url");
        var imageThumbnailUrl = GetString(institution, "image_thumbnail_url");
        var createdDate = GetNullableDate(institution, "created_date");
        var updatedDate = GetNullableDate(institution, "updated_date");
        var rawJsonHash = Sha256Hex(rawJson);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingInstitutionId = await GetExistingInstitutionIdAsync(connection, transaction, openAlexId, cancellationToken);
            bool inserted = existingInstitutionId is null;
            bool updated = existingInstitutionId is not null;
            long institutionId;

            if (inserted)
            {
                institutionId = await InsertInstitutionAsync(
                    connection,
                    transaction,
                    ingestionRunId,
                    openAlexId,
                    openAlexShortId,
                    ror,
                    displayName,
                    countryCode,
                    continent,
                    type,
                    isGlobalSouth,
                    isSuperSystem,
                    worksCount,
                    citedByCount,
                    homepageUrl,
                    imageUrl,
                    imageThumbnailUrl,
                    createdDate,
                    updatedDate,
                    rawJson,
                    rawJsonHash,
                    cancellationToken);
            }
            else
            {
                institutionId = existingInstitutionId.Value;

                await UpdateInstitutionAsync(
                    connection,
                    transaction,
                    institutionId,
                    ingestionRunId,
                    openAlexShortId,
                    ror,
                    displayName,
                    countryCode,
                    continent,
                    type,
                    isGlobalSouth,
                    isSuperSystem,
                    worksCount,
                    citedByCount,
                    homepageUrl,
                    imageUrl,
                    imageThumbnailUrl,
                    createdDate,
                    updatedDate,
                    rawJson,
                    rawJsonHash,
                    cancellationToken);
            }

            await UpsertInstitutionIdsAsync(
                connection,
                transaction,
                institutionId,
                openAlexId,
                ids.ValueKind == JsonValueKind.Object ? ids : default,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new InstitutionUpsertResult
            {
                InstitutionId = institutionId,
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
    InstitutionsProcessed,
    InstitutionsInserted,
    InstitutionsUpdated,
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

    private static async Task<long?> GetExistingInstitutionIdAsync(SqlConnection connection, SqlTransaction transaction, string openAlexId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT InstitutionId FROM openalex.Institution WHERE OpenAlexId = @OpenAlexId;";
        command.Parameters.AddWithValue("@OpenAlexId", openAlexId);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static async Task<long> InsertInstitutionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long ingestionRunId,
        string openAlexId,
        string? openAlexShortId,
        string? ror,
        string? displayName,
        string? countryCode,
        string? continent,
        string? type,
        bool? isGlobalSouth,
        bool? isSuperSystem,
        int? worksCount,
        int? citedByCount,
        string? homepageUrl,
        string? imageUrl,
        string? imageThumbnailUrl,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO openalex.Institution
(
    OpenAlexId,
    OpenAlexShortId,
    Ror,
    DisplayName,
    CountryCode,
    Continent,
    InstitutionType,
    IsGlobalSouth,
    IsSuperSystem,
    WorksCount,
    CitedByCount,
    HomepageUrl,
    ImageUrl,
    ImageThumbnailUrl,
    CreatedDate,
    UpdatedDate,
    RawJson,
    RawJsonHash,
    CreatedAtUtc,
    UpdatedAtUtc,
    LastSeenAtUtc,
    LastIngestionRunId
)
OUTPUT INSERTED.InstitutionId
VALUES
(
    @OpenAlexId,
    @OpenAlexShortId,
    @Ror,
    @DisplayName,
    @CountryCode,
    @Continent,
    @InstitutionType,
    @IsGlobalSouth,
    @IsSuperSystem,
    @WorksCount,
    @CitedByCount,
    @HomepageUrl,
    @ImageUrl,
    @ImageThumbnailUrl,
    @CreatedDate,
    @UpdatedDate,
    @RawJson,
    @RawJsonHash,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    @LastIngestionRunId
);";

        AddInstitutionParameters(command, ingestionRunId, openAlexId, openAlexShortId, ror, displayName, countryCode, continent, type, isGlobalSouth, isSuperSystem, worksCount, citedByCount, homepageUrl, imageUrl, imageThumbnailUrl, createdDate, updatedDate, rawJson, rawJsonHash);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpdateInstitutionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long institutionId,
        long ingestionRunId,
        string? openAlexShortId,
        string? ror,
        string? displayName,
        string? countryCode,
        string? continent,
        string? type,
        bool? isGlobalSouth,
        bool? isSuperSystem,
        int? worksCount,
        int? citedByCount,
        string? homepageUrl,
        string? imageUrl,
        string? imageThumbnailUrl,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE openalex.Institution
SET
    OpenAlexShortId = @OpenAlexShortId,
    Ror = @Ror,
    DisplayName = @DisplayName,
    CountryCode = @CountryCode,
    Continent = @Continent,
    InstitutionType = @InstitutionType,
    IsGlobalSouth = @IsGlobalSouth,
    IsSuperSystem = @IsSuperSystem,
    WorksCount = @WorksCount,
    CitedByCount = @CitedByCount,
    HomepageUrl = @HomepageUrl,
    ImageUrl = @ImageUrl,
    ImageThumbnailUrl = @ImageThumbnailUrl,
    CreatedDate = @CreatedDate,
    UpdatedDate = @UpdatedDate,
    RawJson = @RawJson,
    RawJsonHash = @RawJsonHash,
    UpdatedAtUtc = SYSUTCDATETIME(),
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastIngestionRunId = @LastIngestionRunId
WHERE InstitutionId = @InstitutionId;";

        command.Parameters.AddWithValue("@InstitutionId", institutionId);
        AddInstitutionParameters(command, ingestionRunId, null, openAlexShortId, ror, displayName, countryCode, continent, type, isGlobalSouth, isSuperSystem, worksCount, citedByCount, homepageUrl, imageUrl, imageThumbnailUrl, createdDate, updatedDate, rawJson, rawJsonHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddInstitutionParameters(
        SqlCommand command,
        long ingestionRunId,
        string? openAlexId,
        string? openAlexShortId,
        string? ror,
        string? displayName,
        string? countryCode,
        string? continent,
        string? type,
        bool? isGlobalSouth,
        bool? isSuperSystem,
        int? worksCount,
        int? citedByCount,
        string? homepageUrl,
        string? imageUrl,
        string? imageThumbnailUrl,
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
        command.Parameters.AddWithValue("@Ror", (object?)ror ?? DBNull.Value);
        command.Parameters.AddWithValue("@DisplayName", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("@CountryCode", (object?)countryCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@Continent", (object?)continent ?? DBNull.Value);
        command.Parameters.AddWithValue("@InstitutionType", (object?)type ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsGlobalSouth", (object?)isGlobalSouth ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsSuperSystem", (object?)isSuperSystem ?? DBNull.Value);
        command.Parameters.AddWithValue("@WorksCount", (object?)worksCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@CitedByCount", (object?)citedByCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@HomepageUrl", (object?)homepageUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@ImageThumbnailUrl", (object?)imageThumbnailUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedDate", (object?)createdDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@UpdatedDate", (object?)updatedDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@RawJson", rawJson);
        command.Parameters.AddWithValue("@RawJsonHash", rawJsonHash);
        command.Parameters.AddWithValue("@LastIngestionRunId", ingestionRunId);
    }

    private static async Task UpsertInstitutionIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long institutionId,
        string institutionOpenAlexId,
        JsonElement ids,
        CancellationToken cancellationToken)
    {
        var openAlex = GetString(ids, "openalex");
        var ror = GetString(ids, "ror");
        var wikidata = GetString(ids, "wikidata");
        var wikipedia = GetString(ids, "wikipedia");
        var mag = GetString(ids, "mag");
        var grid = GetString(ids, "grid");
        var rawJson = ids.ValueKind == JsonValueKind.Object ? ids.GetRawText() : null;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
IF EXISTS (SELECT 1 FROM openalex.InstitutionIds WHERE InstitutionId = @InstitutionId)
BEGIN
    UPDATE openalex.InstitutionIds
    SET
        InstitutionOpenAlexId = @InstitutionOpenAlexId,
        OpenAlex = @OpenAlex,
        Ror = @Ror,
        Wikidata = @Wikidata,
        Wikipedia = @Wikipedia,
        Mag = @Mag,
        Grid = @Grid,
        RawJson = @RawJson
    WHERE InstitutionId = @InstitutionId;
END
ELSE
BEGIN
    INSERT INTO openalex.InstitutionIds
    (
        InstitutionId,
        InstitutionOpenAlexId,
        OpenAlex,
        Ror,
        Wikidata,
        Wikipedia,
        Mag,
        Grid,
        RawJson,
        CreatedAtUtc
    )
    VALUES
    (
        @InstitutionId,
        @InstitutionOpenAlexId,
        @OpenAlex,
        @Ror,
        @Wikidata,
        @Wikipedia,
        @Mag,
        @Grid,
        @RawJson,
        SYSUTCDATETIME()
    );
END;";

        command.Parameters.AddWithValue("@InstitutionId", institutionId);
        command.Parameters.AddWithValue("@InstitutionOpenAlexId", institutionOpenAlexId);
        command.Parameters.AddWithValue("@OpenAlex", (object?)openAlex ?? DBNull.Value);
        command.Parameters.AddWithValue("@Ror", (object?)ror ?? DBNull.Value);
        command.Parameters.AddWithValue("@Wikidata", (object?)wikidata ?? DBNull.Value);
        command.Parameters.AddWithValue("@Wikipedia", (object?)wikipedia ?? DBNull.Value);
        command.Parameters.AddWithValue("@Mag", (object?)mag ?? DBNull.Value);
        command.Parameters.AddWithValue("@Grid", (object?)grid ?? DBNull.Value);
        command.Parameters.AddWithValue("@RawJson", (object?)rawJson ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static string? GetNestedString(JsonElement element, string parentName, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(parentName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parent, propertyName);
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

    private static bool? GetNullableBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
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
