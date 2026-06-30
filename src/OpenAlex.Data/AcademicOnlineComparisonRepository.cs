using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class AcademicOnlineComparisonRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcademicOnlineComparisonRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> StartRunAsync(
        string runMode,
        string? pilotCohortName,
        int maxFacultyToProcess,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AcademicOnlineComparisonRun", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.AcademicOnlineComparisonRun does not exist. Run sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql first.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlineComparisonRun
(
    RunMode,
    PilotCohortName,
    MaxFacultyToProcess,
    StartedBy,
    ApplicationVersion,
    RunStatus,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AcademicOnlineComparisonRunId
VALUES
(
    @RunMode,
    @PilotCohortName,
    @MaxFacultyToProcess,
    @StartedBy,
    @ApplicationVersion,
    'Running',
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@RunMode", runMode);
        AddNullable(command, "@PilotCohortName", System.Data.SqlDbType.NVarChar, 200, pilotCohortName);
        command.Parameters.AddWithValue("@MaxFacultyToProcess", maxFacultyToProcess);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task SaveScholarSnapshotAsync(
        long runId,
        long facultyId,
        string facultyDisplayName,
        string? departmentName,
        string? academicOnlineAuthorId,
        string? academicOnlineName,
        string? academicOnlineEmail,
        string? academicOnlinePosition,
        string? academicOnlineOrcidUrl,
        int? publicationCount,
        bool? hasVerifiedPublications,
        decimal? searchRankingScore,
        string searchRawJson,
        string? generalRawJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlineScholarSnapshot
(
    AcademicOnlineComparisonRunId,
    FacultyId,
    FacultyDisplayName,
    DepartmentName,
    AcademicOnlineAuthorId,
    AcademicOnlineName,
    AcademicOnlineEmail,
    AcademicOnlinePosition,
    AcademicOnlineOrcidUrl,
    PublicationCount,
    HasVerifiedPublications,
    SearchRankingScore,
    SearchRawJson,
    GeneralRawJson,
    CreatedAtUtc
)
VALUES
(
    @RunId,
    @FacultyId,
    @FacultyDisplayName,
    @DepartmentName,
    @AcademicOnlineAuthorId,
    @AcademicOnlineName,
    @AcademicOnlineEmail,
    @AcademicOnlinePosition,
    @AcademicOnlineOrcidUrl,
    @PublicationCount,
    @HasVerifiedPublications,
    @SearchRankingScore,
    @SearchRawJson,
    @GeneralRawJson,
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", facultyDisplayName);
        AddNullable(command, "@DepartmentName", System.Data.SqlDbType.NVarChar, 512, departmentName);
        AddNullable(command, "@AcademicOnlineAuthorId", System.Data.SqlDbType.NVarChar, 128, academicOnlineAuthorId);
        AddNullable(command, "@AcademicOnlineName", System.Data.SqlDbType.NVarChar, 512, academicOnlineName);
        AddNullable(command, "@AcademicOnlineEmail", System.Data.SqlDbType.NVarChar, 512, academicOnlineEmail);
        AddNullable(command, "@AcademicOnlinePosition", System.Data.SqlDbType.NVarChar, 512, academicOnlinePosition);
        AddNullable(command, "@AcademicOnlineOrcidUrl", System.Data.SqlDbType.NVarChar, 512, academicOnlineOrcidUrl);
        AddNullable(command, "@PublicationCount", System.Data.SqlDbType.Int, publicationCount);
        AddNullable(command, "@HasVerifiedPublications", System.Data.SqlDbType.Bit, hasVerifiedPublications);
        AddNullable(command, "@SearchRankingScore", System.Data.SqlDbType.Decimal, searchRankingScore);
        AddNullable(command, "@SearchRawJson", System.Data.SqlDbType.NVarChar, -1, searchRawJson);
        AddNullable(command, "@GeneralRawJson", System.Data.SqlDbType.NVarChar, -1, generalRawJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SavePublicationSnapshotAsync(
        long runId,
        long facultyId,
        string academicOnlineAuthorId,
        bool isVerifiedQuery,
        string? academicOnlinePublicationId,
        string? title,
        string? venue,
        string? year,
        int? publicationYear,
        int? citationCount,
        string? doi,
        string? openAlexId,
        bool? verified,
        string? link,
        string? coauthorsJson,
        string? keywordsJson,
        string rawJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcademicOnlinePublicationSnapshot
(
    AcademicOnlineComparisonRunId,
    FacultyId,
    AcademicOnlineAuthorId,
    IsVerifiedQuery,
    AcademicOnlinePublicationId,
    Title,
    Venue,
    YearText,
    PublicationYear,
    CitationCount,
    Doi,
    OpenAlexId,
    Verified,
    Link,
    CoauthorsJson,
    KeywordsJson,
    RawJson,
    CreatedAtUtc
)
VALUES
(
    @RunId,
    @FacultyId,
    @AcademicOnlineAuthorId,
    @IsVerifiedQuery,
    @AcademicOnlinePublicationId,
    @Title,
    @Venue,
    @YearText,
    @PublicationYear,
    @CitationCount,
    @Doi,
    @OpenAlexId,
    @Verified,
    @Link,
    @CoauthorsJson,
    @KeywordsJson,
    @RawJson,
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@AcademicOnlineAuthorId", academicOnlineAuthorId);
        command.Parameters.AddWithValue("@IsVerifiedQuery", isVerifiedQuery);
        AddNullable(command, "@AcademicOnlinePublicationId", System.Data.SqlDbType.NVarChar, 128, academicOnlinePublicationId);
        AddNullable(command, "@Title", System.Data.SqlDbType.NVarChar, -1, title);
        AddNullable(command, "@Venue", System.Data.SqlDbType.NVarChar, 512, venue);
        AddNullable(command, "@YearText", System.Data.SqlDbType.NVarChar, 50, year);
        AddNullable(command, "@PublicationYear", System.Data.SqlDbType.Int, publicationYear);
        AddNullable(command, "@CitationCount", System.Data.SqlDbType.Int, citationCount);
        AddNullable(command, "@Doi", System.Data.SqlDbType.NVarChar, 512, doi);
        AddNullable(command, "@OpenAlexId", System.Data.SqlDbType.NVarChar, 128, openAlexId);
        AddNullable(command, "@Verified", System.Data.SqlDbType.Bit, verified);
        AddNullable(command, "@Link", System.Data.SqlDbType.NVarChar, 1024, link);
        AddNullable(command, "@CoauthorsJson", System.Data.SqlDbType.NVarChar, -1, coauthorsJson);
        AddNullable(command, "@KeywordsJson", System.Data.SqlDbType.NVarChar, -1, keywordsJson);
        AddNullable(command, "@RawJson", System.Data.SqlDbType.NVarChar, -1, rawJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(long runId, string summaryText, CancellationToken cancellationToken = default)
    {
        await UpdateRunStatusAsync(runId, "Completed", summaryText, null, cancellationToken);
    }

    public async Task FailRunAsync(long runId, Exception exception, CancellationToken cancellationToken = default)
    {
        await UpdateRunStatusAsync(runId, "Failed", null, exception.ToString(), cancellationToken);
    }

    private async Task UpdateRunStatusAsync(long runId, string status, string? summaryText, string? errorText, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcademicOnlineComparisonRun
SET
    RunStatus = @RunStatus,
    SummaryText = @SummaryText,
    ErrorText = @ErrorText,
    CompletedAtUtc = CASE WHEN @RunStatus IN ('Completed', 'Failed') THEN SYSUTCDATETIME() ELSE CompletedAtUtc END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineComparisonRunId = @RunId;";
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@RunStatus", status);
        AddNullable(command, "@SummaryText", System.Data.SqlDbType.NVarChar, -1, summaryText);
        AddNullable(command, "@ErrorText", System.Data.SqlDbType.NVarChar, -1, errorText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void AddNullable(SqlCommand command, string name, System.Data.SqlDbType type, object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }

    private static void AddNullable(SqlCommand command, string name, System.Data.SqlDbType type, int size, string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }
}
