using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcademicOnlineReportingStoreRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcademicOnlineReportingStoreRepository(
        SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> StartRunAsync(
        string departmentName,
        int? fromYear,
        int? toYear,
        bool verifiedOnly,
        int maxScholarsToProcess,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await EnsureStoreExistsAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO academiconline.SyncRun
(
    SyncType,
    DepartmentName,
    FromYear,
    ToYear,
    VerifiedOnly,
    MaxScholarsToProcess,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AcademicOnlineSyncRunId
VALUES
(
    N'DepartmentRecentPublications',
    @DepartmentName,
    @FromYear,
    @ToYear,
    @VerifiedOnly,
    @MaxScholarsToProcess,
    N'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@DepartmentName", departmentName);
        AddNullable(command, "@FromYear", SqlDbType.Int, fromYear);
        AddNullable(command, "@ToYear", SqlDbType.Int, toYear);
        command.Parameters.AddWithValue("@VerifiedOnly", verifiedOnly);
        command.Parameters.AddWithValue("@MaxScholarsToProcess", maxScholarsToProcess);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<long> UpsertDepartmentAsync(
        long runId,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        var normalized = sourceName.Trim();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var update = connection.CreateCommand())
        {
            update.CommandText = @"
UPDATE academiconline.Department
SET
    CanonicalName = @CanonicalName,
    IsActive = 1,
    LastRetrievedAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE SourceName = @SourceName;";
            update.Parameters.AddWithValue("@CanonicalName", normalized);
            update.Parameters.AddWithValue("@RunId", runId);
            update.Parameters.AddWithValue("@SourceName", normalized);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = @"
IF NOT EXISTS
(
    SELECT 1
    FROM academiconline.Department
    WHERE SourceName = @SourceName
)
BEGIN
    INSERT INTO academiconline.Department
    (
        SourceName,
        CanonicalName,
        IsActive,
        FirstSeenAtUtc,
        LastRetrievedAtUtc,
        LastSyncRunId
    )
    VALUES
    (
        @SourceName,
        @CanonicalName,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @RunId
    );
END;";
            insert.Parameters.AddWithValue("@SourceName", normalized);
            insert.Parameters.AddWithValue("@CanonicalName", normalized);
            insert.Parameters.AddWithValue("@RunId", runId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.CommandText = @"
SELECT DepartmentId
FROM academiconline.Department
WHERE SourceName = @SourceName;";
        select.Parameters.AddWithValue("@SourceName", normalized);

        return Convert.ToInt64(
            await select.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpsertScholarAsync(
        long runId,
        string academicOnlineAuthorId,
        string displayName,
        string? email,
        string? position,
        string? institution,
        string? orcidUrl,
        int? publicationCount,
        bool? hasPublications,
        bool? hasVerifiedPublications,
        string? rawJson,
        CancellationToken cancellationToken = default)
    {
        var userId = TryGetUvaUserId(email);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.Scholar
SET
    UvaUserId = @UvaUserId,
    DisplayName = @DisplayName,
    Email = @Email,
    Position = @Position,
    Institution = @Institution,
    OrcidUrl = @OrcidUrl,
    PublicationCount = @PublicationCount,
    HasPublications = @HasPublications,
    HasVerifiedPublications = @HasVerifiedPublications,
    RawJson = @RawJson,
    LastRetrievedAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE AcademicOnlineAuthorId = @AuthorId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO academiconline.Scholar
    (
        AcademicOnlineAuthorId,
        UvaUserId,
        DisplayName,
        Email,
        Position,
        Institution,
        OrcidUrl,
        PublicationCount,
        HasPublications,
        HasVerifiedPublications,
        RawJson,
        FirstSeenAtUtc,
        LastRetrievedAtUtc,
        LastSyncRunId
    )
    VALUES
    (
        @AuthorId,
        @UvaUserId,
        @DisplayName,
        @Email,
        @Position,
        @Institution,
        @OrcidUrl,
        @PublicationCount,
        @HasPublications,
        @HasVerifiedPublications,
        @RawJson,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @RunId
    );
END;";

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@AuthorId", academicOnlineAuthorId.Trim());
        command.Parameters.AddWithValue("@DisplayName", displayName.Trim());
        AddNullable(command, "@UvaUserId", SqlDbType.NVarChar, 32, userId);
        AddNullable(command, "@Email", SqlDbType.NVarChar, 512, email);
        AddNullable(command, "@Position", SqlDbType.NVarChar, 512, position);
        AddNullable(command, "@Institution", SqlDbType.NVarChar, 512, institution);
        AddNullable(command, "@OrcidUrl", SqlDbType.NVarChar, 512, orcidUrl);
        AddNullable(command, "@PublicationCount", SqlDbType.Int, publicationCount);
        AddNullable(command, "@HasPublications", SqlDbType.Bit, hasPublications);
        AddNullable(command, "@HasVerifiedPublications", SqlDbType.Bit, hasVerifiedPublications);
        AddNullable(command, "@RawJson", SqlDbType.NVarChar, -1, rawJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchScholarDepartmentAsync(
        long runId,
        string academicOnlineAuthorId,
        long departmentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.ScholarDepartment
SET
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE AcademicOnlineAuthorId = @AuthorId
  AND DepartmentId = @DepartmentId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO academiconline.ScholarDepartment
    (
        AcademicOnlineAuthorId,
        DepartmentId,
        FirstSeenAtUtc,
        LastSeenAtUtc,
        LastSyncRunId
    )
    VALUES
    (
        @AuthorId,
        @DepartmentId,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @RunId
    );
END;";

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@AuthorId", academicOnlineAuthorId.Trim());
        command.Parameters.AddWithValue("@DepartmentId", departmentId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> UpsertPublicationAsync(
        long runId,
        string? academicOnlinePublicationId,
        string? openAlexId,
        string? doi,
        string? title,
        string? venue,
        string? yearText,
        int? publicationYear,
        int? citationCount,
        bool? verified,
        string? link,
        string? coauthorsJson,
        string? keywordsJson,
        string rawJson,
        CancellationToken cancellationToken = default)
    {
        var normalizedAcademicOnlineId = NormalizeOptional(academicOnlinePublicationId);
        var normalizedOpenAlexId = NormalizeOpenAlexId(openAlexId);
        var normalizedDoi = NormalizeDoi(doi);
        var titleYearFingerprint = BuildTitleYearFingerprint(
            title,
            publicationYear,
            yearText);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        long? publicationKey = null;

        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = @"
SELECT TOP (1)
    PublicationKey
FROM academiconline.Publication
WHERE
       (@AcademicOnlinePublicationId IS NOT NULL
        AND AcademicOnlinePublicationId = @AcademicOnlinePublicationId)
    OR (@OpenAlexId IS NOT NULL
        AND OpenAlexId = @OpenAlexId)
    OR (@DoiNormalized IS NOT NULL
        AND DoiNormalized = @DoiNormalized)
    OR (@TitleYearFingerprint IS NOT NULL
        AND TitleYearFingerprint = @TitleYearFingerprint)
ORDER BY
    CASE
        WHEN @AcademicOnlinePublicationId IS NOT NULL
             AND AcademicOnlinePublicationId = @AcademicOnlinePublicationId THEN 1
        WHEN @OpenAlexId IS NOT NULL
             AND OpenAlexId = @OpenAlexId THEN 2
        WHEN @DoiNormalized IS NOT NULL
             AND DoiNormalized = @DoiNormalized THEN 3
        ELSE 4
    END,
    PublicationKey;";

            AddNullable(
                lookup,
                "@AcademicOnlinePublicationId",
                SqlDbType.NVarChar,
                128,
                normalizedAcademicOnlineId);
            AddNullable(
                lookup,
                "@OpenAlexId",
                SqlDbType.NVarChar,
                128,
                normalizedOpenAlexId);
            AddNullable(
                lookup,
                "@DoiNormalized",
                SqlDbType.NVarChar,
                512,
                normalizedDoi);
            AddNullable(
                lookup,
                "@TitleYearFingerprint",
                SqlDbType.NVarChar,
                900,
                titleYearFingerprint);

            var value = await lookup.ExecuteScalarAsync(cancellationToken);
            if (value is not null && value != DBNull.Value)
            {
                publicationKey = Convert.ToInt64(value);
            }
        }

        if (publicationKey.HasValue)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = @"
UPDATE academiconline.Publication
SET
    AcademicOnlinePublicationId =
        COALESCE(@AcademicOnlinePublicationId, AcademicOnlinePublicationId),
    OpenAlexId = COALESCE(@OpenAlexId, OpenAlexId),
    Doi = COALESCE(@Doi, Doi),
    DoiNormalized = COALESCE(@DoiNormalized, DoiNormalized),
    Title = COALESCE(@Title, Title),
    TitleYearFingerprint =
        COALESCE(@TitleYearFingerprint, TitleYearFingerprint),
    Venue = COALESCE(@Venue, Venue),
    YearText = COALESCE(@YearText, YearText),
    PublicationYear = COALESCE(@PublicationYear, PublicationYear),
    CitationCount = COALESCE(@CitationCount, CitationCount),
    Verified = COALESCE(@Verified, Verified),
    Link = COALESCE(@Link, Link),
    CoauthorsJson = COALESCE(@CoauthorsJson, CoauthorsJson),
    KeywordsJson = COALESCE(@KeywordsJson, KeywordsJson),
    RawJson = @RawJson,
    LastRetrievedAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE PublicationKey = @PublicationKey;";

            AddPublicationParameters(
                update,
                runId,
                publicationKey.Value,
                normalizedAcademicOnlineId,
                normalizedOpenAlexId,
                doi,
                normalizedDoi,
                title,
                titleYearFingerprint,
                venue,
                yearText,
                publicationYear,
                citationCount,
                verified,
                link,
                coauthorsJson,
                keywordsJson,
                rawJson);

            await update.ExecuteNonQueryAsync(cancellationToken);
            return publicationKey.Value;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO academiconline.Publication
(
    AcademicOnlinePublicationId,
    OpenAlexId,
    Doi,
    DoiNormalized,
    Title,
    TitleYearFingerprint,
    Venue,
    YearText,
    PublicationYear,
    CitationCount,
    Verified,
    Link,
    CoauthorsJson,
    KeywordsJson,
    RawJson,
    FirstSeenAtUtc,
    LastRetrievedAtUtc,
    LastSyncRunId
)
OUTPUT INSERTED.PublicationKey
VALUES
(
    @AcademicOnlinePublicationId,
    @OpenAlexId,
    @Doi,
    @DoiNormalized,
    @Title,
    @TitleYearFingerprint,
    @Venue,
    @YearText,
    @PublicationYear,
    @CitationCount,
    @Verified,
    @Link,
    @CoauthorsJson,
    @KeywordsJson,
    @RawJson,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    @RunId
);";

        AddPublicationParameters(
            insert,
            runId,
            publicationKey: null,
            normalizedAcademicOnlineId,
            normalizedOpenAlexId,
            doi,
            normalizedDoi,
            title,
            titleYearFingerprint,
            venue,
            yearText,
            publicationYear,
            citationCount,
            verified,
            link,
            coauthorsJson,
            keywordsJson,
            rawJson);

        return Convert.ToInt64(
            await insert.ExecuteScalarAsync(cancellationToken));
    }

    public async Task TouchScholarPublicationAsync(
        long runId,
        string academicOnlineAuthorId,
        long publicationKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.ScholarPublication
SET
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE AcademicOnlineAuthorId = @AuthorId
  AND PublicationKey = @PublicationKey;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO academiconline.ScholarPublication
    (
        AcademicOnlineAuthorId,
        PublicationKey,
        FirstSeenAtUtc,
        LastSeenAtUtc,
        LastSyncRunId
    )
    VALUES
    (
        @AuthorId,
        @PublicationKey,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @RunId
    );
END;";

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@AuthorId", academicOnlineAuthorId.Trim());
        command.Parameters.AddWithValue("@PublicationKey", publicationKey);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchDepartmentPublicationAsync(
        long runId,
        long departmentId,
        long publicationKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.DepartmentPublication
SET
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE DepartmentId = @DepartmentId
  AND PublicationKey = @PublicationKey;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO academiconline.DepartmentPublication
    (
        DepartmentId,
        PublicationKey,
        IsCurrent,
        FirstSeenAtUtc,
        LastSeenAtUtc,
        LastSyncRunId,
        CurrentSnapshotRunId
    )
    VALUES
    (
        @DepartmentId,
        @PublicationKey,
        0,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @RunId,
        NULL
    );
END;";

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@DepartmentId", departmentId);
        command.Parameters.AddWithValue("@PublicationKey", publicationKey);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchDepartmentScholarPublicationAsync(
        long runId,
        long departmentId,
        string academicOnlineAuthorId,
        long publicationKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.DepartmentScholarPublication
SET
    LastSeenAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE DepartmentId = @DepartmentId
  AND AcademicOnlineAuthorId = @AuthorId
  AND PublicationKey = @PublicationKey;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO academiconline.DepartmentScholarPublication
    (
        DepartmentId,
        AcademicOnlineAuthorId,
        PublicationKey,
        IsCurrent,
        FirstSeenAtUtc,
        LastSeenAtUtc,
        LastSyncRunId,
        CurrentSnapshotRunId
    )
    VALUES
    (
        @DepartmentId,
        @AuthorId,
        @PublicationKey,
        0,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @RunId,
        NULL
    );
END;";

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@DepartmentId", departmentId);
        command.Parameters.AddWithValue("@AuthorId", academicOnlineAuthorId.Trim());
        command.Parameters.AddWithValue("@PublicationKey", publicationKey);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PromoteDepartmentSnapshotAsync(
        long runId,
        long departmentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText = @"
IF NOT EXISTS
(
    SELECT 1
    FROM academiconline.SyncRun
    WHERE AcademicOnlineSyncRunId = @RunId
      AND RunStatus = N'Completed'
      AND IsCacheComplete = 1
)
BEGIN
    RAISERROR(
        N'Only a completed, cache-complete Academic Online sync may be promoted to the current department snapshot.',
        16,
        1);
    RETURN;
END;

UPDATE academiconline.ScholarDepartment
SET
    IsCurrent =
        CASE
            WHEN LastSyncRunId = @RunId THEN 1
            ELSE 0
        END,
    CurrentSnapshotRunId = @RunId
WHERE DepartmentId = @DepartmentId;

UPDATE academiconline.DepartmentPublication
SET
    IsCurrent =
        CASE
            WHEN LastSyncRunId = @RunId THEN 1
            ELSE 0
        END,
    CurrentSnapshotRunId = @RunId
WHERE DepartmentId = @DepartmentId;

UPDATE academiconline.DepartmentScholarPublication
SET
    IsCurrent =
        CASE
            WHEN LastSyncRunId = @RunId THEN 1
            ELSE 0
        END,
    CurrentSnapshotRunId = @RunId
WHERE DepartmentId = @DepartmentId;

UPDATE academiconline.Department
SET
    CurrentSnapshotRunId = @RunId,
    CurrentSnapshotAtUtc = SYSUTCDATETIME(),
    LastRetrievedAtUtc = SYSUTCDATETIME(),
    LastSyncRunId = @RunId
WHERE DepartmentId = @DepartmentId;

UPDATE academiconline.SyncRun
SET
    SnapshotPromoted = 1,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineSyncRunId = @RunId;";

            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@DepartmentId", departmentId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task CompleteRunAsync(
        long runId,
        int scholarsDiscovered,
        int scholarsProcessed,
        int publicationsProcessed,
        int scholarPagesFetched,
        int publicationPagesFetched,
        bool scholarPaginationSafeguardHit,
        int publicationPaginationSafeguardHitCount,
        bool isCacheComplete,
        string summaryText,
        CancellationToken cancellationToken = default)
    {
        return UpdateRunAsync(
            runId,
            "Completed",
            scholarsDiscovered,
            scholarsProcessed,
            publicationsProcessed,
            scholarPagesFetched,
            publicationPagesFetched,
            scholarPaginationSafeguardHit,
            publicationPaginationSafeguardHitCount,
            isCacheComplete,
            summaryText,
            errorText: null,
            cancellationToken);
    }

    public Task FailRunAsync(
        long runId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        return UpdateRunAsync(
            runId,
            "Failed",
            scholarsDiscovered: null,
            scholarsProcessed: 0,
            publicationsProcessed: 0,
            scholarPagesFetched: 0,
            publicationPagesFetched: 0,
            scholarPaginationSafeguardHit: false,
            publicationPaginationSafeguardHitCount: 0,
            isCacheComplete: null,
            summaryText: null,
            errorText: exception.ToString(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentListItem>>
        GetDepartmentsAsync(
            CancellationToken cancellationToken = default)
    {
        var rows = new List<AcademicOnlineDepartmentListItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.Department",
                "U",
                cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    d.DepartmentId,
    d.CanonicalName,
    ScholarCount = COUNT(DISTINCT sd.AcademicOnlineAuthorId),
    PublicationCount = COUNT(DISTINCT dp.PublicationKey),
    LastRetrievedAtUtc =
        COALESCE(d.CurrentSnapshotAtUtc, d.LastRetrievedAtUtc),
    d.CurrentSnapshotRunId,
    snapshot.ScholarsDiscovered,
    snapshot.ScholarsProcessed,
    snapshot.MaxScholarsToProcess,
    snapshot.ScholarPaginationSafeguardHit,
    snapshot.PublicationPaginationSafeguardHitCount,
    snapshot.IsCacheComplete,
    snapshot.FromYear,
    snapshot.ToYear,
    snapshot.VerifiedOnly,
    d.CurrentSnapshotAtUtc
FROM academiconline.Department d
LEFT JOIN academiconline.ScholarDepartment sd
    ON sd.DepartmentId = d.DepartmentId
   AND sd.IsCurrent = 1
   AND sd.CurrentSnapshotRunId = d.CurrentSnapshotRunId
LEFT JOIN academiconline.DepartmentPublication dp
    ON dp.DepartmentId = d.DepartmentId
   AND dp.IsCurrent = 1
   AND dp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
LEFT JOIN academiconline.SyncRun snapshot
    ON snapshot.AcademicOnlineSyncRunId = d.CurrentSnapshotRunId
WHERE d.IsActive = 1
GROUP BY
    d.DepartmentId,
    d.CanonicalName,
    d.LastRetrievedAtUtc,
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotAtUtc,
    snapshot.ScholarsDiscovered,
    snapshot.ScholarsProcessed,
    snapshot.MaxScholarsToProcess,
    snapshot.ScholarPaginationSafeguardHit,
    snapshot.PublicationPaginationSafeguardHitCount,
    snapshot.IsCacheComplete,
    snapshot.FromYear,
    snapshot.ToYear,
    snapshot.VerifiedOnly
ORDER BY d.CanonicalName;";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcademicOnlineDepartmentListItem
            {
                DepartmentId = reader.GetInt64(0),
                DepartmentName = reader.GetString(1),
                ScholarCount = reader.GetInt32(2),
                PublicationCount = reader.GetInt32(3),
                LastRetrievedAtUtc = reader.GetDateTime(4),
                CurrentSnapshotRunId = GetNullableLong(reader, 5),
                ScholarsDiscovered = GetNullableInt(reader, 6),
                ScholarsProcessed = GetNullableInt(reader, 7),
                MaxScholarsToProcess = GetNullableInt(reader, 8),
                ScholarPaginationSafeguardHit = GetNullableBool(reader, 9) ?? false,
                PublicationPaginationSafeguardHitCount = GetNullableInt(reader, 10) ?? 0,
                IsCacheComplete = GetNullableBool(reader, 11),
                SyncFromYear = GetNullableInt(reader, 12),
                SyncToYear = GetNullableInt(reader, 13),
                SyncVerifiedOnly = GetNullableBool(reader, 14),
                CurrentSnapshotAtUtc = GetNullableDateTime(reader, 15)
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentPublicationRow>>
        GetNewestPublicationsByDepartmentAsync(
            long departmentId,
            int top,
            int? fromYear,
            bool verifiedOnly,
            CancellationToken cancellationToken = default)
    {
        var rows = new List<AcademicOnlineDepartmentPublicationRow>();
        var limit = Math.Clamp(top, 1, 500);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.vw_DepartmentNewestPublication",
                "V",
                cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@Top)
    DepartmentId,
    DepartmentName,
    PublicationKey,
    AcademicOnlinePublicationId,
    OpenAlexId,
    Doi,
    Title,
    Venue,
    YearText,
    PublicationYear,
    CitationCount,
    Verified,
    Link,
    FacultyCount,
    FacultyNames,
    LastRetrievedAtUtc
FROM academiconline.vw_DepartmentNewestPublication
WHERE DepartmentId = @DepartmentId
  AND (@FromYear IS NULL OR PublicationYear >= @FromYear)
  AND (@VerifiedOnly = 0 OR Verified = 1)
ORDER BY
    PublicationYear DESC,
    PublicationKey DESC;";

        command.Parameters.AddWithValue("@Top", limit);
        command.Parameters.AddWithValue("@DepartmentId", departmentId);
        AddNullable(command, "@FromYear", SqlDbType.Int, fromYear);
        command.Parameters.AddWithValue("@VerifiedOnly", verifiedOnly);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcademicOnlineDepartmentPublicationRow
            {
                DepartmentId = reader.GetInt64(0),
                DepartmentName = reader.GetString(1),
                PublicationKey = reader.GetInt64(2),
                AcademicOnlinePublicationId = GetNullableString(reader, 3),
                OpenAlexId = GetNullableString(reader, 4),
                Doi = GetNullableString(reader, 5),
                Title = GetNullableString(reader, 6) ?? "(Untitled publication)",
                Venue = GetNullableString(reader, 7),
                YearText = GetNullableString(reader, 8),
                PublicationYear = GetNullableInt(reader, 9),
                CitationCount = GetNullableInt(reader, 10),
                Verified = GetNullableBool(reader, 11),
                Link = GetNullableString(reader, 12),
                FacultyCount = reader.GetInt32(13),
                FacultyNames = GetNullableString(reader, 14) ?? string.Empty,
                LastRetrievedAtUtc = reader.GetDateTime(15)
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentSourceUnit>>
        GetDepartmentSourceUnitsAsync(
            long departmentId,
            CancellationToken cancellationToken = default)
    {
        var rows = new List<AcademicOnlineDepartmentSourceUnit>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.DepartmentSourceUnit",
                "U",
                cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    DepartmentSourceUnitId,
    DepartmentId,
    OfficialUnitName,
    AcademicOnlineSearchName,
    SearchPriority
FROM academiconline.DepartmentSourceUnit
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1
ORDER BY
    OfficialUnitName,
    SearchPriority,
    AcademicOnlineSearchName;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            departmentId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new AcademicOnlineDepartmentSourceUnit
                {
                    SourceUnitId = reader.GetInt64(0),
                    DepartmentId = reader.GetInt64(1),
                    OfficialUnitName = reader.GetString(2),
                    AcademicOnlineSearchName = reader.GetString(3),
                    SearchPriority = reader.GetInt32(4)
                });
        }

        return rows;
    }

    public async Task RecordSourceUnitAttemptAsync(
        long sourceUnitId,
        string status,
        int scholarCount,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.DepartmentSourceUnit
SET
    LastAttemptAtUtc = SYSUTCDATETIME(),
    LastAttemptStatus = @Status,
    LastScholarCount = @ScholarCount,
    LastError = @ErrorMessage,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentSourceUnitId = @SourceUnitId;";

        command.Parameters.AddWithValue(
            "@SourceUnitId",
            sourceUnitId);
        command.Parameters.AddWithValue(
            "@Status",
            status);
        command.Parameters.AddWithValue(
            "@ScholarCount",
            Math.Max(0, scholarCount));
        AddNullable(
            command,
            "@ErrorMessage",
            SqlDbType.NVarChar,
            -1,
            errorMessage);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentRosterMember>>
        GetDepartmentRosterMembersAsync(
            long departmentId,
            CancellationToken cancellationToken = default)
    {
        var rows =
            new List<AcademicOnlineDepartmentRosterMember>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.DepartmentRosterMember",
                "U",
                cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    DepartmentRosterMemberId,
    DepartmentId,
    DisplayName,
    ExpectedEmail,
    AcademicOnlineAuthorId,
    OpenAlexAuthorId,
    SearchPriority
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1
ORDER BY
    SearchPriority,
    DisplayName;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            departmentId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new AcademicOnlineDepartmentRosterMember
                {
                    RosterMemberId = reader.GetInt64(0),
                    DepartmentId = reader.GetInt64(1),
                    DisplayName = reader.GetString(2),
                    ExpectedEmail =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetString(3),
                    AcademicOnlineAuthorId =
                        reader.IsDBNull(4)
                            ? null
                            : reader.GetString(4),
                    OpenAlexAuthorId =
                        reader.IsDBNull(5)
                            ? null
                            : reader.GetString(5),
                    SearchPriority = reader.GetInt32(6)
                });
        }

        return rows;
    }

    public async Task<IReadOnlyList<AcademicOnlineCoauthorIdentityCandidate>>
        FindCoauthorIdentityCandidatesAsync(
            string token1,
            string token2,
            CancellationToken cancellationToken = default)
    {
        var rows =
            new List<AcademicOnlineCoauthorIdentityCandidate>();

        if (string.IsNullOrWhiteSpace(token1) ||
            string.IsNullOrWhiteSpace(token2))
        {
            return rows;
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT
    j.[id] AS AcademicOnlineAuthorId,
    CoauthorName = MIN(j.[name]),
    EvidencePublicationCount =
        COUNT(DISTINCT p.PublicationKey),
    EarliestPublicationYear =
        MIN(p.PublicationYear),
    LatestPublicationYear =
        MAX(p.PublicationYear)
FROM academiconline.Publication p
CROSS APPLY OPENJSON(p.CoauthorsJson)
WITH
(
    [name] NVARCHAR(512) '$.name',
    [id]   NVARCHAR(128) '$.id'
) j
WHERE p.CoauthorsJson IS NOT NULL
  AND ISJSON(p.CoauthorsJson) = 1
  AND j.[id] IS NOT NULL
  AND LTRIM(RTRIM(j.[id])) <> N''
  AND LOWER(j.[name]) LIKE @Token1
  AND LOWER(j.[name]) LIKE @Token2
GROUP BY j.[id]
ORDER BY
    COUNT(DISTINCT p.PublicationKey) DESC,
    MIN(j.[name]),
    j.[id];";

        command.Parameters.AddWithValue(
            "@Token1",
            "%" + token1.Trim().ToLowerInvariant() + "%");

        command.Parameters.AddWithValue(
            "@Token2",
            "%" + token2.Trim().ToLowerInvariant() + "%");

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
            cancellationToken))
        {
            rows.Add(
                new AcademicOnlineCoauthorIdentityCandidate
                {
                    AcademicOnlineAuthorId =
                        reader.GetString(0),
                    CoauthorName =
                        reader.IsDBNull(1)
                            ? string.Empty
                            : reader.GetString(1),
                    EvidencePublicationCount =
                        reader.GetInt32(2),
                    EarliestPublicationYear =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetInt32(3),
                    LatestPublicationYear =
                        reader.IsDBNull(4)
                            ? null
                            : reader.GetInt32(4)
                });
        }

        return rows;
    }

    public async Task RecordRosterMemberOpenAlexFallbackAsync(
        long rosterMemberId,
        string? openAlexAuthorId,
        string status,
        string? evidence,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
UPDATE academiconline.DepartmentRosterMember
SET
    OpenAlexAuthorId =
        COALESCE(@OpenAlexAuthorId, OpenAlexAuthorId),
    OpenAlexResolutionStatus = @Status,
    OpenAlexResolutionEvidence = @Evidence,
    OpenAlexLastResolvedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentRosterMemberId = @RosterMemberId;";

        command.Parameters.AddWithValue(
            "@RosterMemberId",
            rosterMemberId);

        AddNullable(
            command,
            "@OpenAlexAuthorId",
            SqlDbType.NVarChar,
            128,
            openAlexAuthorId);

        command.Parameters.AddWithValue(
            "@Status",
            status);

        AddNullable(
            command,
            "@Evidence",
            SqlDbType.NVarChar,
            -1,
            evidence);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task RecordRosterMemberResolutionAsync(
        long rosterMemberId,
        string? academicOnlineAuthorId,
        string status,
        string? errorMessage,
        string? resolutionMethod = null,
        string? resolutionEvidence = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.DepartmentRosterMember
SET
    AcademicOnlineAuthorId =
        COALESCE(@AcademicOnlineAuthorId, AcademicOnlineAuthorId),
    LastResolvedAtUtc = SYSUTCDATETIME(),
    ResolutionStatus = @Status,
    ResolutionError = @ErrorMessage,
    ResolutionMethod =
        COALESCE(@ResolutionMethod, ResolutionMethod),
    ResolutionEvidence =
        COALESCE(@ResolutionEvidence, ResolutionEvidence),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentRosterMemberId = @RosterMemberId;";

        command.Parameters.AddWithValue(
            "@RosterMemberId",
            rosterMemberId);
        AddNullable(
            command,
            "@AcademicOnlineAuthorId",
            SqlDbType.NVarChar,
            128,
            academicOnlineAuthorId);
        command.Parameters.AddWithValue(
            "@Status",
            status);
        AddNullable(
            command,
            "@ErrorMessage",
            SqlDbType.NVarChar,
            -1,
            errorMessage);
        AddNullable(
            command,
            "@ResolutionMethod",
            SqlDbType.NVarChar,
            50,
            resolutionMethod);
        AddNullable(
            command,
            "@ResolutionEvidence",
            SqlDbType.NVarChar,
            -1,
            resolutionEvidence);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateRunAsync(
        long runId,
        string status,
        int? scholarsDiscovered,
        int scholarsProcessed,
        int publicationsProcessed,
        int scholarPagesFetched,
        int publicationPagesFetched,
        bool scholarPaginationSafeguardHit,
        int publicationPaginationSafeguardHitCount,
        bool? isCacheComplete,
        string? summaryText,
        string? errorText,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.SyncRun
SET
    RunStatus = @RunStatus,
    ScholarsDiscovered = @ScholarsDiscovered,
    ScholarsProcessed = @ScholarsProcessed,
    ScholarPaginationSafeguardHit = @ScholarPaginationSafeguardHit,
    PublicationPaginationSafeguardHitCount = @PublicationPaginationSafeguardHitCount,
    IsCacheComplete = @IsCacheComplete,
    PublicationsProcessed = @PublicationsProcessed,
    ScholarPagesFetched = @ScholarPagesFetched,
    PublicationPagesFetched = @PublicationPagesFetched,
    SummaryText = @SummaryText,
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcademicOnlineSyncRunId = @RunId;";

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@RunStatus", status);
        AddNullable(command, "@ScholarsDiscovered", SqlDbType.Int, scholarsDiscovered);
        command.Parameters.AddWithValue("@ScholarsProcessed", scholarsProcessed);
        command.Parameters.AddWithValue("@ScholarPaginationSafeguardHit", scholarPaginationSafeguardHit);
        command.Parameters.AddWithValue("@PublicationPaginationSafeguardHitCount", publicationPaginationSafeguardHitCount);
        AddNullable(command, "@IsCacheComplete", SqlDbType.Bit, isCacheComplete);
        command.Parameters.AddWithValue("@PublicationsProcessed", publicationsProcessed);
        command.Parameters.AddWithValue("@ScholarPagesFetched", scholarPagesFetched);
        command.Parameters.AddWithValue("@PublicationPagesFetched", publicationPagesFetched);
        AddNullable(command, "@SummaryText", SqlDbType.NVarChar, -1, summaryText);
        AddNullable(command, "@ErrorText", SqlDbType.NVarChar, -1, errorText);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddPublicationParameters(
        SqlCommand command,
        long runId,
        long? publicationKey,
        string? academicOnlinePublicationId,
        string? openAlexId,
        string? doi,
        string? doiNormalized,
        string? title,
        string? titleYearFingerprint,
        string? venue,
        string? yearText,
        int? publicationYear,
        int? citationCount,
        bool? verified,
        string? link,
        string? coauthorsJson,
        string? keywordsJson,
        string rawJson)
    {
        command.Parameters.AddWithValue("@RunId", runId);

        if (publicationKey.HasValue)
        {
            command.Parameters.AddWithValue(
                "@PublicationKey",
                publicationKey.Value);
        }

        AddNullable(
            command,
            "@AcademicOnlinePublicationId",
            SqlDbType.NVarChar,
            128,
            academicOnlinePublicationId);
        AddNullable(
            command,
            "@OpenAlexId",
            SqlDbType.NVarChar,
            128,
            openAlexId);
        AddNullable(command, "@Doi", SqlDbType.NVarChar, 512, doi);
        AddNullable(
            command,
            "@DoiNormalized",
            SqlDbType.NVarChar,
            512,
            doiNormalized);
        AddNullable(command, "@Title", SqlDbType.NVarChar, -1, title);
        AddNullable(
            command,
            "@TitleYearFingerprint",
            SqlDbType.NVarChar,
            900,
            titleYearFingerprint);
        AddNullable(command, "@Venue", SqlDbType.NVarChar, 512, venue);
        AddNullable(command, "@YearText", SqlDbType.NVarChar, 50, yearText);
        AddNullable(command, "@PublicationYear", SqlDbType.Int, publicationYear);
        AddNullable(command, "@CitationCount", SqlDbType.Int, citationCount);
        AddNullable(command, "@Verified", SqlDbType.Bit, verified);
        AddNullable(command, "@Link", SqlDbType.NVarChar, 1024, link);
        AddNullable(
            command,
            "@CoauthorsJson",
            SqlDbType.NVarChar,
            -1,
            coauthorsJson);
        AddNullable(
            command,
            "@KeywordsJson",
            SqlDbType.NVarChar,
            -1,
            keywordsJson);
        AddNullable(command, "@RawJson", SqlDbType.NVarChar, -1, rawJson);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeOpenAlexId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim()
            .Replace(
                "https://openalex.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://openalex.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToUpperInvariant();
    }

    private static string? NormalizeDoi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        foreach (var prefix in new[]
                 {
                     "https://doi.org/",
                     "http://doi.org/",
                     "https://dx.doi.org/",
                     "http://dx.doi.org/",
                     "doi:"
                 })
        {
            if (normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        normalized = normalized.Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

    private static string? BuildTitleYearFingerprint(
        string? title,
        int? publicationYear,
        string? yearText)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var year = publicationYear?.ToString()
            ?? NormalizeOptional(yearText);

        if (string.IsNullOrWhiteSpace(year))
        {
            return null;
        }

        var characters = title
            .Trim()
            .ToLowerInvariant()
            .Select(character =>
                char.IsLetterOrDigit(character) ||
                char.IsWhiteSpace(character)
                    ? character
                    : ' ')
            .ToArray();

        var normalizedTitle = string.Join(
            " ",
            new string(characters)
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));

        var fingerprint = normalizedTitle + "|" + year.Trim();

        return fingerprint.Length <= 900
            ? fingerprint
            : fingerprint[..900];
    }

    private static string? TryGetUvaUserId(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        const string suffix = "@virginia.edu";
        var trimmed = email.Trim();

        if (!trimmed.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var userId = trimmed[..^suffix.Length].Trim();

        return userId.Length >= 2 &&
               userId.Length <= 32 &&
               userId.All(char.IsLetterOrDigit)
            ? userId.ToLowerInvariant()
            : null;
    }

    private static async Task EnsureStoreExistsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await ObjectExistsAsync(
                connection,
                "academiconline.SyncRun",
                "U",
                cancellationToken))
        {
            throw new InvalidOperationException(
                "academiconline.SyncRun does not exist. Run sql/411_V3E_PATCH019_AcademicOnlineLocalReportingStore_SQL2016.sql first.");
        }
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void AddNullable(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }

    private static void AddNullable(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value =
            string.IsNullOrWhiteSpace(value)
                ? DBNull.Value
                : value;
    }

    private static string? GetNullableString(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static int? GetNullableInt(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);
    }

    private static long? GetNullableLong(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt64(ordinal);
    }

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetDateTime(ordinal);
    }

    private static bool? GetNullableBool(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetBoolean(ordinal);
    }
}
