using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcademicOnlineDepartmentDiscoveryRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcademicOnlineDepartmentDiscoveryRepository(
        SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentDiscoverySeed>>
        GetActiveSeedsAsync(
            string parentName,
            CancellationToken cancellationToken = default)
    {
        var rows =
            new List<AcademicOnlineDepartmentDiscoverySeed>();

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.DepartmentDiscoverySeed",
                "U",
                cancellationToken))
        {
            return rows;
        }

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT
    DepartmentDiscoverySeedId,
    ParentName,
    OfficialName,
    AcademicOnlineSearchName,
    DepartmentCategory,
    SearchPriority
FROM academiconline.DepartmentDiscoverySeed
WHERE ParentName = @ParentName
  AND IsActive = 1
ORDER BY
    DepartmentCategory,
    OfficialName,
    SearchPriority;";

        command.Parameters.AddWithValue(
            "@ParentName",
            parentName.Trim());

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
            cancellationToken))
        {
            rows.Add(
                new AcademicOnlineDepartmentDiscoverySeed
                {
                    SeedId = reader.GetInt64(0),
                    ParentName = reader.GetString(1),
                    OfficialName = reader.GetString(2),
                    AcademicOnlineSearchName =
                        reader.GetString(3),
                    DepartmentCategory =
                        reader.GetString(4),
                    SearchPriority =
                        reader.GetInt32(5)
                });
        }

        return rows;
    }

    public async Task<long> QueueRequestAsync(
        string parentName,
        int rowsPerPage,
        int maxPages,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            throw new InvalidOperationException(
                "Parent organization name is required.");
        }

        var pageSize = Math.Clamp(rowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages, 1, 1000);

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @ExistingId BIGINT;

SELECT TOP (1)
    @ExistingId = DepartmentDiscoveryRequestId
FROM academiconline.DepartmentDiscoveryRequest
WHERE ParentName = @ParentName
  AND RequestStatus IN (N'Pending', N'Running')
ORDER BY DepartmentDiscoveryRequestId DESC;

IF @ExistingId IS NOT NULL
BEGIN
    SELECT @ExistingId;
    RETURN;
END;

INSERT INTO academiconline.DepartmentDiscoveryRequest
(
    ParentName,
    RowsPerPage,
    MaxPages,
    RequestStatus,
    RequestedBy,
    RequestedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @ParentName,
    @RowsPerPage,
    @MaxPages,
    N'Pending',
    @RequestedBy,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

SELECT CONVERT(BIGINT, SCOPE_IDENTITY());";

        command.Parameters.AddWithValue(
            "@ParentName",
            parentName.Trim());

        command.Parameters.AddWithValue(
            "@RowsPerPage",
            pageSize);

        command.Parameters.AddWithValue(
            "@MaxPages",
            pageLimit);

        command.Parameters.AddWithValue(
            "@RequestedBy",
            string.IsNullOrWhiteSpace(requestedBy)
                ? "OpenAlex.Admin"
                : requestedBy.Trim());

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<AcademicOnlineDepartmentDiscoveryStatus?>
        GetLatestStatusAsync(
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.DepartmentDiscoveryRequest",
                "U",
                cancellationToken))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (1)
    q.DepartmentDiscoveryRequestId,
    q.ParentName,
    q.RowsPerPage,
    q.MaxPages,
    q.RequestStatus,
    q.RequestedBy,
    q.RequestedAtUtc,
    q.StartedAtUtc,
    q.CompletedAtUtc,
    q.LeaseOwner,
    q.LeaseUntilUtc,
    q.DiscoveryRunId,
    q.ErrorText,
    r.RunStatus,
    r.PagesFetched,
    r.ScholarsScanned,
    r.AffiliationsObserved,
    r.DepartmentsDiscovered,
    r.DepartmentsInserted,
    r.DepartmentsUpdated,
    r.HitMaxPages,
    r.SummaryText
FROM academiconline.DepartmentDiscoveryRequest q
LEFT JOIN academiconline.DepartmentDiscoveryRun r
    ON r.DepartmentDiscoveryRunId = q.DiscoveryRunId
ORDER BY q.DepartmentDiscoveryRequestId DESC;";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AcademicOnlineDepartmentDiscoveryStatus
        {
            RequestId = reader.GetInt64(0),
            ParentName = reader.GetString(1),
            RowsPerPage = reader.GetInt32(2),
            MaxPages = reader.GetInt32(3),
            RequestStatus = reader.GetString(4),
            RequestedBy = reader.GetString(5),
            RequestedAtUtc = reader.GetDateTime(6),
            StartedAtUtc =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetDateTime(7),
            CompletedAtUtc =
                reader.IsDBNull(8)
                    ? null
                    : reader.GetDateTime(8),
            LeaseOwner =
                reader.IsDBNull(9)
                    ? null
                    : reader.GetString(9),
            LeaseUntilUtc =
                reader.IsDBNull(10)
                    ? null
                    : reader.GetDateTime(10),
            DiscoveryRunId =
                reader.IsDBNull(11)
                    ? null
                    : reader.GetInt64(11),
            ErrorText =
                reader.IsDBNull(12)
                    ? null
                    : reader.GetString(12),
            RunStatus =
                reader.IsDBNull(13)
                    ? null
                    : reader.GetString(13),
            PagesFetched =
                reader.IsDBNull(14)
                    ? null
                    : reader.GetInt32(14),
            ScholarsScanned =
                reader.IsDBNull(15)
                    ? null
                    : reader.GetInt32(15),
            AffiliationsObserved =
                reader.IsDBNull(16)
                    ? null
                    : reader.GetInt32(16),
            DepartmentsDiscovered =
                reader.IsDBNull(17)
                    ? null
                    : reader.GetInt32(17),
            DepartmentsInserted =
                reader.IsDBNull(18)
                    ? null
                    : reader.GetInt32(18),
            DepartmentsUpdated =
                reader.IsDBNull(19)
                    ? null
                    : reader.GetInt32(19),
            HitMaxPages =
                reader.IsDBNull(20)
                    ? null
                    : reader.GetBoolean(20),
            SummaryText =
                reader.IsDBNull(21)
                    ? null
                    : reader.GetString(21)
        };
    }

    public async Task<AcademicOnlineDepartmentDiscoveryClaim?>
        ClaimPendingRequestAsync(
            string leaseOwner,
            int leaseMinutes,
            CancellationToken cancellationToken = default)
    {
        var leaseDuration = Math.Clamp(
            leaseMinutes,
            5,
            1440);

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.DepartmentDiscoveryRequest",
                "U",
                cancellationToken))
        {
            return null;
        }

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DECLARE @Claimed TABLE
(
    RequestId BIGINT,
    ParentName NVARCHAR(512),
    RowsPerPage INT,
    MaxPages INT,
    RequestedBy NVARCHAR(256),
    LeaseOwner NVARCHAR(256),
    LeaseUntilUtc DATETIME2(3)
);

;WITH candidate AS
(
    SELECT TOP (1)
        *
    FROM academiconline.DepartmentDiscoveryRequest
        WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE
        RequestStatus = N'Pending'
        OR
        (
            RequestStatus = N'Running'
            AND LeaseUntilUtc < SYSUTCDATETIME()
        )
    ORDER BY DepartmentDiscoveryRequestId
)
UPDATE candidate
SET
    RequestStatus = N'Running',
    StartedAtUtc =
        COALESCE(StartedAtUtc, SYSUTCDATETIME()),
    CompletedAtUtc = NULL,
    LeaseOwner = @LeaseOwner,
    LeaseUntilUtc =
        DATEADD(
            MINUTE,
            @LeaseMinutes,
            SYSUTCDATETIME()),
    ErrorText = NULL,
    UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT
    INSERTED.DepartmentDiscoveryRequestId,
    INSERTED.ParentName,
    INSERTED.RowsPerPage,
    INSERTED.MaxPages,
    INSERTED.RequestedBy,
    INSERTED.LeaseOwner,
    INSERTED.LeaseUntilUtc
INTO @Claimed;

SELECT
    RequestId,
    ParentName,
    RowsPerPage,
    MaxPages,
    RequestedBy,
    LeaseOwner,
    LeaseUntilUtc
FROM @Claimed;";

            command.Parameters.AddWithValue(
                "@LeaseOwner",
                leaseOwner.Trim());

            command.Parameters.AddWithValue(
                "@LeaseMinutes",
                leaseDuration);

            AcademicOnlineDepartmentDiscoveryClaim?
                claim = null;

            await using (var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    claim =
                        new AcademicOnlineDepartmentDiscoveryClaim
                        {
                            RequestId = reader.GetInt64(0),
                            ParentName = reader.GetString(1),
                            RowsPerPage = reader.GetInt32(2),
                            MaxPages = reader.GetInt32(3),
                            RequestedBy = reader.GetString(4),
                            LeaseOwner = reader.GetString(5),
                            LeaseUntilUtc = reader.GetDateTime(6)
                        };
                }
            }

            await transaction.CommitAsync(
                cancellationToken);

            return claim;
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    public async Task<long> StartRunAsync(
        AcademicOnlineDepartmentDiscoveryClaim claim,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO academiconline.DepartmentDiscoveryRun
(
    DepartmentDiscoveryRequestId,
    ParentName,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @RequestId,
    @ParentName,
    N'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

DECLARE @RunId BIGINT =
    CONVERT(BIGINT, SCOPE_IDENTITY());

UPDATE academiconline.DepartmentDiscoveryRequest
SET
    DiscoveryRunId = @RunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentDiscoveryRequestId = @RequestId
  AND LeaseOwner = @LeaseOwner;

IF @@ROWCOUNT = 0
BEGIN
    RAISERROR(
        N'Department discovery request lease is no longer owned by this worker.',
        16,
        1);
END;

SELECT @RunId;";

            command.Parameters.AddWithValue(
                "@RequestId",
                claim.RequestId);

            command.Parameters.AddWithValue(
                "@ParentName",
                claim.ParentName);

            command.Parameters.AddWithValue(
                "@LeaseOwner",
                claim.LeaseOwner);

            command.Parameters.AddWithValue(
                "@StartedBy",
                startedBy);

            command.Parameters.AddWithValue(
                "@ApplicationVersion",
                applicationVersion);

            var runId = Convert.ToInt64(
                await command.ExecuteScalarAsync(
                    cancellationToken));

            await transaction.CommitAsync(
                cancellationToken);

            return runId;
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    public async Task<bool> UpsertDiscoveredDepartmentAsync(
        long discoveryRunId,
        AcademicOnlineDiscoveredDepartmentRecord record,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record.DepartmentName))
        {
            throw new InvalidOperationException(
                "Discovered department name is required.");
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @DepartmentId BIGINT;
DECLARE @WasInserted BIT = 0;

SELECT TOP (1)
    @DepartmentId = DepartmentId
FROM academiconline.Department
WHERE
    (
        @AcademicOnlineDepartmentId IS NOT NULL
        AND AcademicOnlineDepartmentId =
            @AcademicOnlineDepartmentId
    )
    OR
    (
        @AcademicOnlineSlug IS NOT NULL
        AND AcademicOnlineSlug =
            @AcademicOnlineSlug
    )
    OR SourceName = @DepartmentName
ORDER BY
    CASE
        WHEN @AcademicOnlineDepartmentId IS NOT NULL
         AND AcademicOnlineDepartmentId =
             @AcademicOnlineDepartmentId
            THEN 0
        WHEN @AcademicOnlineSlug IS NOT NULL
         AND AcademicOnlineSlug =
             @AcademicOnlineSlug
            THEN 1
        ELSE 2
    END;

IF @DepartmentId IS NULL
BEGIN
    INSERT INTO academiconline.Department
    (
        SourceName,
        CanonicalName,
        IsActive,
        FirstSeenAtUtc,
        LastRetrievedAtUtc,
        AcademicOnlineDepartmentId,
        AcademicOnlineSlug,
        ParentAcademicOnlineDepartmentId,
        ParentName,
        ParentSlug,
        DiscoveryScholarCount,
        DiscoveryFirstSeenAtUtc,
        DiscoveryLastSeenAtUtc,
        LastDiscoveryRunId,
        RefreshEnabled,
        RefreshIntervalHours,
        RefreshFailureRetryMinutes,
        RefreshFromYear,
        RefreshVerifiedOnly,
        RefreshMaxScholars
    )
    VALUES
    (
        @DepartmentName,
        @DepartmentName,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @AcademicOnlineDepartmentId,
        @AcademicOnlineSlug,
        @ParentAcademicOnlineDepartmentId,
        @ParentName,
        @ParentSlug,
        @ScholarCount,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        @DiscoveryRunId,
        0,
        168,
        60,
        YEAR(SYSUTCDATETIME()) - 1,
        0,
        250
    );

    SET @DepartmentId =
        CONVERT(BIGINT, SCOPE_IDENTITY());

    SET @WasInserted = 1;
END;
ELSE
BEGIN
    UPDATE academiconline.Department
    SET
        SourceName = @DepartmentName,
        CanonicalName = @DepartmentName,
        IsActive = 1,
        LastRetrievedAtUtc =
            SYSUTCDATETIME(),
        AcademicOnlineDepartmentId =
            COALESCE(
                @AcademicOnlineDepartmentId,
                AcademicOnlineDepartmentId),
        AcademicOnlineSlug =
            COALESCE(
                @AcademicOnlineSlug,
                AcademicOnlineSlug),
        ParentAcademicOnlineDepartmentId =
            COALESCE(
                @ParentAcademicOnlineDepartmentId,
                ParentAcademicOnlineDepartmentId),
        ParentName =
            COALESCE(
                @ParentName,
                ParentName),
        ParentSlug =
            COALESCE(
                @ParentSlug,
                ParentSlug),
        DiscoveryScholarCount =
            @ScholarCount,
        DiscoveryFirstSeenAtUtc =
            COALESCE(
                DiscoveryFirstSeenAtUtc,
                SYSUTCDATETIME()),
        DiscoveryLastSeenAtUtc =
            SYSUTCDATETIME(),
        LastDiscoveryRunId =
            @DiscoveryRunId
    WHERE DepartmentId = @DepartmentId;
END;

SELECT @WasInserted;";

        AddNullable(
            command,
            "@AcademicOnlineDepartmentId",
            SqlDbType.NVarChar,
            128,
            record.AcademicOnlineDepartmentId);

        AddNullable(
            command,
            "@AcademicOnlineSlug",
            SqlDbType.NVarChar,
            256,
            record.Slug);

        AddNullable(
            command,
            "@ParentAcademicOnlineDepartmentId",
            SqlDbType.NVarChar,
            128,
            record.ParentAcademicOnlineDepartmentId);

        AddNullable(
            command,
            "@ParentName",
            SqlDbType.NVarChar,
            512,
            record.ParentName);

        AddNullable(
            command,
            "@ParentSlug",
            SqlDbType.NVarChar,
            256,
            record.ParentSlug);

        command.Parameters.AddWithValue(
            "@DepartmentName",
            record.DepartmentName.Trim());

        command.Parameters.AddWithValue(
            "@ScholarCount",
            Math.Max(0, record.ScholarCount));

        command.Parameters.AddWithValue(
            "@DiscoveryRunId",
            discoveryRunId);

        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(
                cancellationToken));
    }

    public async Task CompleteAsync(
        AcademicOnlineDepartmentDiscoveryClaim claim,
        long runId,
        string runStatus,
        int pagesFetched,
        int scholarsScanned,
        int affiliationsObserved,
        int departmentsDiscovered,
        int departmentsInserted,
        int departmentsUpdated,
        bool hitMaxPages,
        string summaryText,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE academiconline.DepartmentDiscoveryRun
SET
    RunStatus = @RunStatus,
    PagesFetched = @PagesFetched,
    ScholarsScanned = @ScholarsScanned,
    AffiliationsObserved = @AffiliationsObserved,
    DepartmentsDiscovered = @DepartmentsDiscovered,
    DepartmentsInserted = @DepartmentsInserted,
    DepartmentsUpdated = @DepartmentsUpdated,
    HitMaxPages = @HitMaxPages,
    SummaryText = @SummaryText,
    ErrorText = NULL,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentDiscoveryRunId = @RunId;

UPDATE academiconline.DepartmentDiscoveryRequest
SET
    RequestStatus = @RunStatus,
    CompletedAtUtc = SYSUTCDATETIME(),
    LeaseOwner = NULL,
    LeaseUntilUtc = NULL,
    ErrorText = NULL,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentDiscoveryRequestId = @RequestId
  AND LeaseOwner = @LeaseOwner;

IF @@ROWCOUNT = 0
BEGIN
    RAISERROR(
        N'Department discovery request lease is no longer owned by this worker.',
        16,
        1);
END;";

            command.Parameters.AddWithValue(
                "@RunStatus",
                runStatus);

            command.Parameters.AddWithValue(
                "@PagesFetched",
                pagesFetched);

            command.Parameters.AddWithValue(
                "@ScholarsScanned",
                scholarsScanned);

            command.Parameters.AddWithValue(
                "@AffiliationsObserved",
                affiliationsObserved);

            command.Parameters.AddWithValue(
                "@DepartmentsDiscovered",
                departmentsDiscovered);

            command.Parameters.AddWithValue(
                "@DepartmentsInserted",
                departmentsInserted);

            command.Parameters.AddWithValue(
                "@DepartmentsUpdated",
                departmentsUpdated);

            command.Parameters.AddWithValue(
                "@HitMaxPages",
                hitMaxPages);

            command.Parameters.AddWithValue(
                "@SummaryText",
                summaryText);

            command.Parameters.AddWithValue(
                "@RunId",
                runId);

            command.Parameters.AddWithValue(
                "@RequestId",
                claim.RequestId);

            command.Parameters.AddWithValue(
                "@LeaseOwner",
                claim.LeaseOwner);

            await command.ExecuteNonQueryAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    public async Task FailAsync(
        AcademicOnlineDepartmentDiscoveryClaim claim,
        long? runId,
        Exception exception,
        int pagesFetched = 0,
        int scholarsScanned = 0,
        int affiliationsObserved = 0,
        bool hitMaxPages = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            if (runId.HasValue)
            {
                await using var runCommand =
                    connection.CreateCommand();

                runCommand.Transaction = transaction;
                runCommand.CommandText = @"
UPDATE academiconline.DepartmentDiscoveryRun
SET
    RunStatus = N'Failed',
    PagesFetched = @PagesFetched,
    ScholarsScanned = @ScholarsScanned,
    AffiliationsObserved = @AffiliationsObserved,
    HitMaxPages = @HitMaxPages,
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentDiscoveryRunId = @RunId;";

                runCommand.Parameters.AddWithValue(
                    "@RunId",
                    runId.Value);

                runCommand.Parameters.AddWithValue(
                    "@PagesFetched",
                    Math.Max(0, pagesFetched));

                runCommand.Parameters.AddWithValue(
                    "@ScholarsScanned",
                    Math.Max(0, scholarsScanned));

                runCommand.Parameters.AddWithValue(
                    "@AffiliationsObserved",
                    Math.Max(0, affiliationsObserved));

                runCommand.Parameters.AddWithValue(
                    "@HitMaxPages",
                    hitMaxPages);

                runCommand.Parameters.AddWithValue(
                    "@ErrorText",
                    exception.ToString());

                await runCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using var requestCommand =
                connection.CreateCommand();

            requestCommand.Transaction = transaction;
            requestCommand.CommandText = @"
UPDATE academiconline.DepartmentDiscoveryRequest
SET
    RequestStatus = N'Failed',
    CompletedAtUtc = SYSUTCDATETIME(),
    LeaseOwner = NULL,
    LeaseUntilUtc = NULL,
    ErrorText = @ErrorText,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DepartmentDiscoveryRequestId = @RequestId
  AND LeaseOwner = @LeaseOwner;";

            requestCommand.Parameters.AddWithValue(
                "@RequestId",
                claim.RequestId);

            requestCommand.Parameters.AddWithValue(
                "@LeaseOwner",
                claim.LeaseOwner);

            requestCommand.Parameters.AddWithValue(
                "@ErrorText",
                exception.ToString());

            await requestCommand.ExecuteNonQueryAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
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

        command.Parameters.AddWithValue(
            "@ObjectName",
            objectName);

        command.Parameters.AddWithValue(
            "@ObjectType",
            objectType);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken)) == 1;
    }

    private static void AddNullable(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string? value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                type,
                size);

        parameter.Value =
            string.IsNullOrWhiteSpace(value)
                ? DBNull.Value
                : value.Trim();
    }
}
