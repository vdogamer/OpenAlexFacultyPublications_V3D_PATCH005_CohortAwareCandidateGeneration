using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class WorkIngestionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public WorkIngestionRepository(SqlConnectionFactory connectionFactory)
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
    'Work',
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

        var exceptionText = exception.ToString();

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
        command.Parameters.AddWithValue("@ErrorResponseSnippet", exceptionText[..Math.Min(4000, exceptionText.Length)]);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkUpsertResult> UpsertWorkAsync(
        JsonElement work,
        long ingestionRunId,
        IReadOnlyCollection<string> configuredInstitutionIds,
        CancellationToken cancellationToken = default)
    {
        var rawJson = work.GetRawText();
        var openAlexId = GetString(work, "id");

        if (string.IsNullOrWhiteSpace(openAlexId))
        {
            throw new InvalidOperationException("OpenAlex work row is missing required id.");
        }

        var openAccess = GetObject(work, "open_access");

        var openAlexShortId = GetOpenAlexShortId(openAlexId);
        var doi = GetString(work, "doi");
        var displayName = GetString(work, "display_name");
        var title = GetString(work, "title") ?? displayName;
        var publicationYear = GetNullableInt(work, "publication_year");
        var publicationDate = GetNullableDate(work, "publication_date");
        var workType = Fit(GetString(work, "type"), 100);
        var language = Fit(GetString(work, "language"), 50);
        var citedByCount = GetNullableInt(work, "cited_by_count");
        var authorsCount = GetNullableInt(work, "authors_count");
        var countriesDistinctCount = GetNullableInt(work, "countries_distinct_count");
        var institutionsDistinctCount = GetNullableInt(work, "institutions_distinct_count");
        var isRetracted = GetNullableBool(work, "is_retracted");
        var isParatext = GetNullableBool(work, "is_paratext");
        var openAccessIsOa = openAccess is null ? null : GetNullableBool(openAccess.Value, "is_oa");
        var openAccessOaStatus = openAccess is null ? null : Fit(GetString(openAccess.Value, "oa_status"), 100);
        var openAccessAnyRepositoryHasFulltext = openAccess is null ? null : GetNullableBool(openAccess.Value, "any_repository_has_fulltext");
        var createdDate = GetNullableDate(work, "created_date");
        var updatedDate = GetNullableDate(work, "updated_date");
        var rawJsonHash = Sha256Hex(rawJson);

        var configuredFullIds = configuredInstitutionIds
            .Select(NormalizeInstitutionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingWorkId = await GetExistingWorkIdAsync(connection, transaction, openAlexId, cancellationToken);

            bool inserted = existingWorkId is null;
            bool updated = existingWorkId is not null;
            long workId;

            if (inserted)
            {
                workId = await InsertWorkAsync(
                    connection,
                    transaction,
                    ingestionRunId,
                    openAlexId,
                    openAlexShortId,
                    doi,
                    displayName,
                    title,
                    publicationYear,
                    publicationDate,
                    workType,
                    language,
                    citedByCount,
                    authorsCount,
                    countriesDistinctCount,
                    institutionsDistinctCount,
                    isRetracted,
                    isParatext,
                    openAccessIsOa,
                    openAccessOaStatus,
                    openAccessAnyRepositoryHasFulltext,
                    createdDate,
                    updatedDate,
                    rawJson,
                    rawJsonHash,
                    cancellationToken);
            }
            else
            {
                workId = existingWorkId.Value;

                await UpdateWorkAsync(
                    connection,
                    transaction,
                    workId,
                    ingestionRunId,
                    openAlexShortId,
                    doi,
                    displayName,
                    title,
                    publicationYear,
                    publicationDate,
                    workType,
                    language,
                    citedByCount,
                    authorsCount,
                    countriesDistinctCount,
                    institutionsDistinctCount,
                    isRetracted,
                    isParatext,
                    openAccessIsOa,
                    openAccessOaStatus,
                    openAccessAnyRepositoryHasFulltext,
                    createdDate,
                    updatedDate,
                    rawJson,
                    rawJsonHash,
                    cancellationToken);
            }

            var authorshipResult = await ReplaceWorkAuthorshipsAsync(
                connection,
                transaction,
                workId,
                openAlexId,
                work,
                configuredFullIds,
                cancellationToken);

            var matchedInstitutionRows = await InsertWorkMatchedInstitutionsAsync(
                connection,
                transaction,
                workId,
                openAlexId,
                authorshipResult.MatchedInstitutionIds,
                ingestionRunId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new WorkUpsertResult
            {
                WorkId = workId,
                OpenAlexId = openAlexId,
                Inserted = inserted,
                Updated = updated,
                AuthorshipRows = authorshipResult.AuthorshipRows,
                AuthorshipInstitutionRows = authorshipResult.AuthorshipInstitutionRows,
                MatchedInstitutionRows = matchedInstitutionRows
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
        int authorshipRows,
        int authorshipInstitutionRows,
        int matchedInstitutionRows,
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
    WorksProcessed,
    WorksInserted,
    WorksUpdated,
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

    private static async Task<long?> GetExistingWorkIdAsync(SqlConnection connection, SqlTransaction transaction, string openAlexId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT WorkId FROM openalex.Work WHERE OpenAlexId = @OpenAlexId;";
        command.Parameters.AddWithValue("@OpenAlexId", openAlexId);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static async Task<long> InsertWorkAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long ingestionRunId,
        string openAlexId,
        string? openAlexShortId,
        string? doi,
        string? displayName,
        string? title,
        int? publicationYear,
        DateTime? publicationDate,
        string? workType,
        string? language,
        int? citedByCount,
        int? authorsCount,
        int? countriesDistinctCount,
        int? institutionsDistinctCount,
        bool? isRetracted,
        bool? isParatext,
        bool? openAccessIsOa,
        string? openAccessOaStatus,
        bool? openAccessAnyRepositoryHasFulltext,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO openalex.Work
(
    OpenAlexId,
    OpenAlexShortId,
    Doi,
    DisplayName,
    Title,
    PublicationYear,
    PublicationDate,
    WorkType,
    Language,
    CitedByCount,
    AuthorsCount,
    CountriesDistinctCount,
    InstitutionsDistinctCount,
    IsRetracted,
    IsParatext,
    OpenAccessIsOa,
    OpenAccessOaStatus,
    OpenAccessAnyRepositoryHasFulltext,
    CreatedDate,
    UpdatedDate,
    RawJson,
    RawJsonHash,
    CreatedAtUtc,
    UpdatedAtUtc,
    LastSeenAtUtc,
    LastIngestionRunId
)
OUTPUT INSERTED.WorkId
VALUES
(
    @OpenAlexId,
    @OpenAlexShortId,
    @Doi,
    @DisplayName,
    @Title,
    @PublicationYear,
    @PublicationDate,
    @WorkType,
    @Language,
    @CitedByCount,
    @AuthorsCount,
    @CountriesDistinctCount,
    @InstitutionsDistinctCount,
    @IsRetracted,
    @IsParatext,
    @OpenAccessIsOa,
    @OpenAccessOaStatus,
    @OpenAccessAnyRepositoryHasFulltext,
    @CreatedDate,
    @UpdatedDate,
    @RawJson,
    @RawJsonHash,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    @LastIngestionRunId
);";

        AddWorkParameters(command, ingestionRunId, openAlexId, openAlexShortId, doi, displayName, title, publicationYear, publicationDate, workType, language, citedByCount, authorsCount, countriesDistinctCount, institutionsDistinctCount, isRetracted, isParatext, openAccessIsOa, openAccessOaStatus, openAccessAnyRepositoryHasFulltext, createdDate, updatedDate, rawJson, rawJsonHash);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpdateWorkAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long workId,
        long ingestionRunId,
        string? openAlexShortId,
        string? doi,
        string? displayName,
        string? title,
        int? publicationYear,
        DateTime? publicationDate,
        string? workType,
        string? language,
        int? citedByCount,
        int? authorsCount,
        int? countriesDistinctCount,
        int? institutionsDistinctCount,
        bool? isRetracted,
        bool? isParatext,
        bool? openAccessIsOa,
        string? openAccessOaStatus,
        bool? openAccessAnyRepositoryHasFulltext,
        DateTime? createdDate,
        DateTime? updatedDate,
        string rawJson,
        string rawJsonHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE openalex.Work
SET
    OpenAlexShortId = @OpenAlexShortId,
    Doi = @Doi,
    DisplayName = @DisplayName,
    Title = @Title,
    PublicationYear = @PublicationYear,
    PublicationDate = @PublicationDate,
    WorkType = @WorkType,
    Language = @Language,
    CitedByCount = @CitedByCount,
    AuthorsCount = @AuthorsCount,
    CountriesDistinctCount = @CountriesDistinctCount,
    InstitutionsDistinctCount = @InstitutionsDistinctCount,
    IsRetracted = @IsRetracted,
    IsParatext = @IsParatext,
    OpenAccessIsOa = @OpenAccessIsOa,
    OpenAccessOaStatus = @OpenAccessOaStatus,
    OpenAccessAnyRepositoryHasFulltext = @OpenAccessAnyRepositoryHasFulltext,
    CreatedDate = @CreatedDate,
    UpdatedDate = @UpdatedDate,
    RawJson = @RawJson,
    RawJsonHash = @RawJsonHash,
    UpdatedAtUtc = SYSUTCDATETIME(),
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastIngestionRunId = @LastIngestionRunId
WHERE WorkId = @WorkId;";

        command.Parameters.AddWithValue("@WorkId", workId);
        AddWorkParameters(command, ingestionRunId, null, openAlexShortId, doi, displayName, title, publicationYear, publicationDate, workType, language, citedByCount, authorsCount, countriesDistinctCount, institutionsDistinctCount, isRetracted, isParatext, openAccessIsOa, openAccessOaStatus, openAccessAnyRepositoryHasFulltext, createdDate, updatedDate, rawJson, rawJsonHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddWorkParameters(
        SqlCommand command,
        long ingestionRunId,
        string? openAlexId,
        string? openAlexShortId,
        string? doi,
        string? displayName,
        string? title,
        int? publicationYear,
        DateTime? publicationDate,
        string? workType,
        string? language,
        int? citedByCount,
        int? authorsCount,
        int? countriesDistinctCount,
        int? institutionsDistinctCount,
        bool? isRetracted,
        bool? isParatext,
        bool? openAccessIsOa,
        string? openAccessOaStatus,
        bool? openAccessAnyRepositoryHasFulltext,
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
        command.Parameters.AddWithValue("@Doi", (object?)doi ?? DBNull.Value);
        command.Parameters.AddWithValue("@DisplayName", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
        command.Parameters.AddWithValue("@PublicationYear", (object?)publicationYear ?? DBNull.Value);
        command.Parameters.AddWithValue("@PublicationDate", (object?)publicationDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@WorkType", (object?)workType ?? DBNull.Value);
        command.Parameters.AddWithValue("@Language", (object?)language ?? DBNull.Value);
        command.Parameters.AddWithValue("@CitedByCount", (object?)citedByCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@AuthorsCount", (object?)authorsCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@CountriesDistinctCount", (object?)countriesDistinctCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@InstitutionsDistinctCount", (object?)institutionsDistinctCount ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsRetracted", (object?)isRetracted ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsParatext", (object?)isParatext ?? DBNull.Value);
        command.Parameters.AddWithValue("@OpenAccessIsOa", (object?)openAccessIsOa ?? DBNull.Value);
        command.Parameters.AddWithValue("@OpenAccessOaStatus", (object?)openAccessOaStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@OpenAccessAnyRepositoryHasFulltext", (object?)openAccessAnyRepositoryHasFulltext ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedDate", (object?)createdDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@UpdatedDate", (object?)updatedDate?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@RawJson", rawJson);
        command.Parameters.AddWithValue("@RawJsonHash", rawJsonHash);
        command.Parameters.AddWithValue("@LastIngestionRunId", ingestionRunId);
    }

    private static async Task<AuthorshipReplaceResult> ReplaceWorkAuthorshipsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long workId,
        string workOpenAlexId,
        JsonElement work,
        HashSet<string> configuredInstitutionIds,
        CancellationToken cancellationToken)
    {
        await using (var deleteInstitutions = connection.CreateCommand())
        {
            deleteInstitutions.Transaction = transaction;
            deleteInstitutions.CommandText = "DELETE FROM openalex.WorkAuthorshipInstitution WHERE WorkId = @WorkId;";
            deleteInstitutions.Parameters.AddWithValue("@WorkId", workId);
            await deleteInstitutions.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteAuthorships = connection.CreateCommand())
        {
            deleteAuthorships.Transaction = transaction;
            deleteAuthorships.CommandText = "DELETE FROM openalex.WorkAuthorship WHERE WorkId = @WorkId;";
            deleteAuthorships.Parameters.AddWithValue("@WorkId", workId);
            await deleteAuthorships.ExecuteNonQueryAsync(cancellationToken);
        }

        var authorshipRows = 0;
        var authorshipInstitutionRows = 0;
        var matchedInstitutionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (work.ValueKind != JsonValueKind.Object ||
            !work.TryGetProperty("authorships", out var authorships) ||
            authorships.ValueKind != JsonValueKind.Array)
        {
            return new AuthorshipReplaceResult(authorshipRows, authorshipInstitutionRows, matchedInstitutionIds);
        }

        var authorshipOrdinal = 0;

        foreach (var authorship in authorships.EnumerateArray())
        {
            if (authorship.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var author = GetObject(authorship, "author");

            var authorOpenAlexId = author is null ? null : GetString(author.Value, "id");
            var authorDisplayName = author is null ? null : Fit(GetString(author.Value, "display_name"), 512);
            var authorOrcid = author is null ? null : GetString(author.Value, "orcid");
            var rawAffiliationString = GetRawAffiliationString(authorship);

            long workAuthorshipId;

            await using (var insertAuthorship = connection.CreateCommand())
            {
                insertAuthorship.Transaction = transaction;
                insertAuthorship.CommandText = @"
INSERT INTO openalex.WorkAuthorship
(
    WorkId,
    WorkOpenAlexId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    IsCorresponding,
    RawAuthorName,
    RawAffiliationString,
    OrdinalPosition,
    RawJson,
    CreatedAtUtc
)
OUTPUT INSERTED.WorkAuthorshipId
VALUES
(
    @WorkId,
    @WorkOpenAlexId,
    @AuthorOpenAlexId,
    @AuthorDisplayName,
    @AuthorOrcid,
    @IsCorresponding,
    @RawAuthorName,
    @RawAffiliationString,
    @OrdinalPosition,
    @RawJson,
    SYSUTCDATETIME()
);";

                insertAuthorship.Parameters.AddWithValue("@WorkId", workId);
                insertAuthorship.Parameters.AddWithValue("@WorkOpenAlexId", workOpenAlexId);
                insertAuthorship.Parameters.AddWithValue("@AuthorOpenAlexId", (object?)authorOpenAlexId ?? DBNull.Value);
                insertAuthorship.Parameters.AddWithValue("@AuthorDisplayName", (object?)authorDisplayName ?? DBNull.Value);
                insertAuthorship.Parameters.AddWithValue("@AuthorOrcid", (object?)authorOrcid ?? DBNull.Value);
                insertAuthorship.Parameters.AddWithValue("@IsCorresponding", (object?)GetNullableBool(authorship, "is_corresponding") ?? DBNull.Value);
                insertAuthorship.Parameters.AddWithValue("@RawAuthorName", (object?)Fit(GetString(authorship, "raw_author_name"), 512) ?? DBNull.Value);
                insertAuthorship.Parameters.AddWithValue("@RawAffiliationString", (object?)rawAffiliationString ?? DBNull.Value);
                insertAuthorship.Parameters.AddWithValue("@OrdinalPosition", authorshipOrdinal);
                insertAuthorship.Parameters.AddWithValue("@RawJson", authorship.GetRawText());

                workAuthorshipId = Convert.ToInt64(await insertAuthorship.ExecuteScalarAsync(cancellationToken));
            }

            authorshipRows++;

            if (authorship.TryGetProperty("institutions", out var institutions) &&
                institutions.ValueKind == JsonValueKind.Array)
            {
                var institutionOrdinal = 0;

                foreach (var institution in institutions.EnumerateArray())
                {
                    if (institution.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var institutionOpenAlexId = GetString(institution, "id");
                    var ids = GetObject(institution, "ids");
                    var geo = GetObject(institution, "geo");

                    if (!string.IsNullOrWhiteSpace(institutionOpenAlexId) &&
                        configuredInstitutionIds.Contains(NormalizeInstitutionId(institutionOpenAlexId)))
                    {
                        matchedInstitutionIds.Add(NormalizeInstitutionId(institutionOpenAlexId));
                    }

                    await using var insertInstitution = connection.CreateCommand();
                    insertInstitution.Transaction = transaction;
                    insertInstitution.CommandText = @"
INSERT INTO openalex.WorkAuthorshipInstitution
(
    WorkAuthorshipId,
    WorkId,
    WorkOpenAlexId,
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
    @WorkAuthorshipId,
    @WorkId,
    @WorkOpenAlexId,
    @InstitutionOpenAlexId,
    @InstitutionDisplayName,
    @InstitutionRor,
    @CountryCode,
    @InstitutionType,
    @OrdinalPosition,
    @RawJson,
    SYSUTCDATETIME()
);";

                    insertInstitution.Parameters.AddWithValue("@WorkAuthorshipId", workAuthorshipId);
                    insertInstitution.Parameters.AddWithValue("@WorkId", workId);
                    insertInstitution.Parameters.AddWithValue("@WorkOpenAlexId", workOpenAlexId);
                    insertInstitution.Parameters.AddWithValue("@InstitutionOpenAlexId", (object?)institutionOpenAlexId ?? DBNull.Value);
                    insertInstitution.Parameters.AddWithValue("@InstitutionDisplayName", (object?)Fit(GetString(institution, "display_name"), 512) ?? DBNull.Value);
                    insertInstitution.Parameters.AddWithValue("@InstitutionRor", (object?)(GetString(institution, "ror") ?? (ids is null ? null : GetString(ids.Value, "ror"))) ?? DBNull.Value);
                    insertInstitution.Parameters.AddWithValue("@CountryCode", (object?)Fit(GetString(institution, "country_code") ?? (geo is null ? null : GetString(geo.Value, "country_code")), 10) ?? DBNull.Value);
                    insertInstitution.Parameters.AddWithValue("@InstitutionType", (object?)Fit(GetString(institution, "type"), 100) ?? DBNull.Value);
                    insertInstitution.Parameters.AddWithValue("@OrdinalPosition", institutionOrdinal);
                    insertInstitution.Parameters.AddWithValue("@RawJson", institution.GetRawText());

                    await insertInstitution.ExecuteNonQueryAsync(cancellationToken);
                    authorshipInstitutionRows++;
                    institutionOrdinal++;
                }
            }

            authorshipOrdinal++;
        }

        return new AuthorshipReplaceResult(authorshipRows, authorshipInstitutionRows, matchedInstitutionIds);
    }

    private static async Task<int> InsertWorkMatchedInstitutionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long workId,
        string workOpenAlexId,
        HashSet<string> matchedInstitutionIds,
        long ingestionRunId,
        CancellationToken cancellationToken)
    {
        var inserted = 0;

        foreach (var institutionOpenAlexId in matchedInstitutionIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
IF NOT EXISTS
(
    SELECT 1
    FROM openalex.WorkMatchedInstitution
    WHERE WorkId = @WorkId
      AND InstitutionOpenAlexId = @InstitutionOpenAlexId
      AND IngestionRunId = @IngestionRunId
)
BEGIN
    INSERT INTO openalex.WorkMatchedInstitution
    (
        WorkId,
        WorkOpenAlexId,
        InstitutionOpenAlexId,
        MatchedFromField,
        MatchSource,
        IngestionRunId,
        CreatedAtUtc
    )
    VALUES
    (
        @WorkId,
        @WorkOpenAlexId,
        @InstitutionOpenAlexId,
        'authorships.institutions.id',
        'ConfiguredInstitutionFilter',
        @IngestionRunId,
        SYSUTCDATETIME()
    );

    SELECT 1;
END
ELSE
BEGIN
    SELECT 0;
END;";

            command.Parameters.AddWithValue("@WorkId", workId);
            command.Parameters.AddWithValue("@WorkOpenAlexId", workOpenAlexId);
            command.Parameters.AddWithValue("@InstitutionOpenAlexId", institutionOpenAlexId);
            command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);

            inserted += Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        return inserted;
    }

    private sealed record AuthorshipReplaceResult(
        int AuthorshipRows,
        int AuthorshipInstitutionRows,
        HashSet<string> MatchedInstitutionIds);

    private static string? GetRawAffiliationString(JsonElement authorship)
    {
        var singular = GetString(authorship, "raw_affiliation_string");
        if (!string.IsNullOrWhiteSpace(singular))
        {
            return singular;
        }

        if (authorship.ValueKind == JsonValueKind.Object &&
            authorship.TryGetProperty("raw_affiliation_strings", out var strings) &&
            strings.ValueKind == JsonValueKind.Array)
        {
            var values = strings.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            return values.Length == 0 ? null : string.Join("; ", values);
        }

        return null;
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

    private static string NormalizeInstitutionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Trim();
        var slashIndex = value.LastIndexOf('/');
        var shortId = slashIndex >= 0 && slashIndex < value.Length - 1
            ? value[(slashIndex + 1)..]
            : value;

        return "https://openalex.org/" + shortId.ToUpperInvariant();
    }

    private static string? Fit(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
