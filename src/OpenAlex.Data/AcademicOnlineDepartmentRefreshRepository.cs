using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcademicOnlineDepartmentRefreshRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcademicOnlineDepartmentRefreshRepository(
        SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentRefreshCatalogItem>>
        GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<AcademicOnlineDepartmentRefreshCatalogItem>();

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
    DepartmentId,
    CanonicalName,
    IsActive,
    RefreshEnabled,
    RefreshIntervalHours,
    RefreshFailureRetryMinutes,
    RefreshFromYear,
    RefreshToYear,
    RefreshVerifiedOnly,
    RefreshMaxScholars,
    CurrentSnapshotRunId,
    CurrentSnapshotAtUtc,
    NextRefreshDueAtUtc,
    LastRefreshAttemptAtUtc,
    LastRefreshAttemptStatus,
    LastRefreshAttemptRunId,
    LastRefreshError,
    RefreshLeaseOwner,
    RefreshLeaseUntilUtc
FROM academiconline.Department
ORDER BY CanonicalName;";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadCatalogItem(reader));
        }

        return rows;
    }

    public async Task UpdatePolicyAsync(
        AcademicOnlineDepartmentRefreshPolicyUpdate policy,
        CancellationToken cancellationToken = default)
    {
        ValidatePolicy(policy);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.Department
SET
    RefreshEnabled = @RefreshEnabled,
    RefreshIntervalHours = @RefreshIntervalHours,
    RefreshFailureRetryMinutes = @RefreshFailureRetryMinutes,
    RefreshFromYear = @RefreshFromYear,
    RefreshToYear = @RefreshToYear,
    RefreshVerifiedOnly = @RefreshVerifiedOnly,
    RefreshMaxScholars = @RefreshMaxScholars,
    NextRefreshDueAtUtc =
        CASE
            WHEN @RefreshEnabled = 1
             AND NextRefreshDueAtUtc IS NULL
                THEN SYSUTCDATETIME()
            ELSE NextRefreshDueAtUtc
        END
WHERE DepartmentId = @DepartmentId;

IF @@ROWCOUNT = 0
BEGIN
    RAISERROR(N'Academic Online department was not found.', 16, 1);
END;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            policy.DepartmentId);
        command.Parameters.AddWithValue(
            "@RefreshEnabled",
            policy.RefreshEnabled);
        command.Parameters.AddWithValue(
            "@RefreshIntervalHours",
            policy.RefreshIntervalHours);
        command.Parameters.AddWithValue(
            "@RefreshFailureRetryMinutes",
            policy.RefreshFailureRetryMinutes);
        AddNullable(
            command,
            "@RefreshFromYear",
            SqlDbType.Int,
            policy.RefreshFromYear);
        AddNullable(
            command,
            "@RefreshToYear",
            SqlDbType.Int,
            policy.RefreshToYear);
        command.Parameters.AddWithValue(
            "@RefreshVerifiedOnly",
            policy.RefreshVerifiedOnly);
        command.Parameters.AddWithValue(
            "@RefreshMaxScholars",
            policy.RefreshMaxScholars);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetDueNowAsync(
        long departmentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.Department
SET
    NextRefreshDueAtUtc = SYSUTCDATETIME()
WHERE DepartmentId = @DepartmentId;

IF @@ROWCOUNT = 0
BEGIN
    RAISERROR(N'Academic Online department was not found.', 16, 1);
END;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            departmentId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AcademicOnlineDepartmentRefreshClaim>>
        ClaimDueDepartmentsAsync(
            int maxDepartments,
            int leaseMinutes,
            string leaseOwner,
            CancellationToken cancellationToken = default)
    {
        var claims = new List<AcademicOnlineDepartmentRefreshClaim>();
        var limit = Math.Clamp(maxDepartments, 1, 50);
        var lease = Math.Clamp(leaseMinutes, 5, 1440);

        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new ArgumentException(
                "Lease owner is required.",
                nameof(leaseOwner));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

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
    DepartmentId BIGINT,
    DepartmentName NVARCHAR(512),
    RefreshIntervalHours INT,
    RefreshFailureRetryMinutes INT,
    RefreshFromYear INT,
    RefreshToYear INT,
    RefreshVerifiedOnly BIT,
    RefreshMaxScholars INT,
    RefreshLeaseOwner NVARCHAR(256),
    RefreshLeaseUntilUtc DATETIME2(3)
);

;WITH due AS
(
    SELECT TOP (@MaxDepartments)
        *
    FROM academiconline.Department WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE IsActive = 1
      AND RefreshEnabled = 1
      AND
      (
          NextRefreshDueAtUtc IS NULL
          OR NextRefreshDueAtUtc <= SYSUTCDATETIME()
      )
      AND
      (
          RefreshLeaseUntilUtc IS NULL
          OR RefreshLeaseUntilUtc < SYSUTCDATETIME()
      )
    ORDER BY
        CASE WHEN NextRefreshDueAtUtc IS NULL THEN 0 ELSE 1 END,
        NextRefreshDueAtUtc,
        DepartmentId
)
UPDATE due
SET
    RefreshLeaseOwner = @LeaseOwner,
    RefreshLeaseUntilUtc =
        DATEADD(MINUTE, @LeaseMinutes, SYSUTCDATETIME()),
    LastRefreshAttemptAtUtc = SYSUTCDATETIME(),
    LastRefreshAttemptStatus = N'Claimed',
    LastRefreshError = NULL
OUTPUT
    INSERTED.DepartmentId,
    INSERTED.CanonicalName,
    INSERTED.RefreshIntervalHours,
    INSERTED.RefreshFailureRetryMinutes,
    INSERTED.RefreshFromYear,
    INSERTED.RefreshToYear,
    INSERTED.RefreshVerifiedOnly,
    INSERTED.RefreshMaxScholars,
    INSERTED.RefreshLeaseOwner,
    INSERTED.RefreshLeaseUntilUtc
INTO @Claimed;

SELECT
    DepartmentId,
    DepartmentName,
    RefreshIntervalHours,
    RefreshFailureRetryMinutes,
    RefreshFromYear,
    RefreshToYear,
    RefreshVerifiedOnly,
    RefreshMaxScholars,
    RefreshLeaseOwner,
    RefreshLeaseUntilUtc
FROM @Claimed
ORDER BY DepartmentId;";

            command.Parameters.AddWithValue(
                "@MaxDepartments",
                limit);
            command.Parameters.AddWithValue(
                "@LeaseMinutes",
                lease);
            command.Parameters.AddWithValue(
                "@LeaseOwner",
                leaseOwner.Trim());

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                claims.Add(new AcademicOnlineDepartmentRefreshClaim
                {
                    DepartmentId = reader.GetInt64(0),
                    DepartmentName = reader.GetString(1),
                    RefreshIntervalHours = reader.GetInt32(2),
                    RefreshFailureRetryMinutes = reader.GetInt32(3),
                    RefreshFromYear =
                        reader.IsDBNull(4)
                            ? null
                            : reader.GetInt32(4),
                    RefreshToYear =
                        reader.IsDBNull(5)
                            ? null
                            : reader.GetInt32(5),
                    RefreshVerifiedOnly = reader.GetBoolean(6),
                    RefreshMaxScholars = reader.GetInt32(7),
                    LeaseOwner = reader.GetString(8),
                    LeaseUntilUtc = reader.GetDateTime(9)
                });
            }

            await transaction.CommitAsync(cancellationToken);
            return claims;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task CompleteClaimAsync(
        long departmentId,
        long? syncRunId,
        bool succeeded,
        bool cacheComplete,
        string leaseOwner,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE academiconline.Department
SET
    LastRefreshAttemptAtUtc = SYSUTCDATETIME(),
    LastRefreshAttemptStatus =
        CASE
            WHEN @Succeeded = 0 THEN N'Failed'
            WHEN @CacheComplete = 1 THEN N'Completed'
            ELSE N'Partial'
        END,
    LastRefreshAttemptRunId = @SyncRunId,
    LastRefreshError = @ErrorMessage,
    NextRefreshDueAtUtc =
        CASE
            WHEN @Succeeded = 1 AND @CacheComplete = 1
                THEN DATEADD
                     (
                         HOUR,
                         RefreshIntervalHours,
                         SYSUTCDATETIME()
                     )
            ELSE DATEADD
                 (
                     MINUTE,
                     RefreshFailureRetryMinutes,
                     SYSUTCDATETIME()
                 )
        END,
    RefreshLeaseOwner = NULL,
    RefreshLeaseUntilUtc = NULL
WHERE DepartmentId = @DepartmentId
  AND RefreshLeaseOwner = @LeaseOwner;

IF @@ROWCOUNT = 0
BEGIN
    RAISERROR(
        N'Department refresh lease is no longer owned by this worker.',
        16,
        1);
END;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            departmentId);
        command.Parameters.AddWithValue(
            "@LeaseOwner",
            leaseOwner.Trim());
        command.Parameters.AddWithValue(
            "@Succeeded",
            succeeded);
        command.Parameters.AddWithValue(
            "@CacheComplete",
            cacheComplete);
        AddNullable(
            command,
            "@SyncRunId",
            SqlDbType.BigInt,
            syncRunId);
        AddNullable(
            command,
            "@ErrorMessage",
            SqlDbType.NVarChar,
            -1,
            errorMessage);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidatePolicy(
        AcademicOnlineDepartmentRefreshPolicyUpdate policy)
    {
        if (policy.DepartmentId <= 0)
        {
            throw new InvalidOperationException(
                "Department ID is required.");
        }

        if (policy.RefreshIntervalHours < 1 ||
            policy.RefreshIntervalHours > 8760)
        {
            throw new InvalidOperationException(
                "Refresh interval must be between 1 and 8760 hours.");
        }

        if (policy.RefreshFailureRetryMinutes < 5 ||
            policy.RefreshFailureRetryMinutes > 1440)
        {
            throw new InvalidOperationException(
                "Failure retry must be between 5 and 1440 minutes.");
        }

        if (policy.RefreshMaxScholars < 1 ||
            policy.RefreshMaxScholars > 5000)
        {
            throw new InvalidOperationException(
                "Maximum scholars must be between 1 and 5000.");
        }

        if (policy.RefreshFromYear.HasValue &&
            policy.RefreshToYear.HasValue &&
            policy.RefreshFromYear > policy.RefreshToYear)
        {
            throw new InvalidOperationException(
                "From year cannot be greater than To year.");
        }

        foreach (var year in new[]
                 {
                     policy.RefreshFromYear,
                     policy.RefreshToYear
                 })
        {
            if (year.HasValue &&
                (year < 1800 ||
                 year > DateTime.UtcNow.Year + 1))
            {
                throw new InvalidOperationException(
                    "Refresh year is outside the allowed range.");
            }
        }
    }

    private static AcademicOnlineDepartmentRefreshCatalogItem
        ReadCatalogItem(SqlDataReader reader)
    {
        return new AcademicOnlineDepartmentRefreshCatalogItem
        {
            DepartmentId = reader.GetInt64(0),
            DepartmentName = reader.GetString(1),
            IsActive = reader.GetBoolean(2),
            RefreshEnabled = reader.GetBoolean(3),
            RefreshIntervalHours = reader.GetInt32(4),
            RefreshFailureRetryMinutes = reader.GetInt32(5),
            RefreshFromYear =
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
            RefreshToYear =
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
            RefreshVerifiedOnly = reader.GetBoolean(8),
            RefreshMaxScholars = reader.GetInt32(9),
            CurrentSnapshotRunId =
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
            CurrentSnapshotAtUtc =
                reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            NextRefreshDueAtUtc =
                reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            LastRefreshAttemptAtUtc =
                reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            LastRefreshAttemptStatus =
                reader.IsDBNull(14) ? null : reader.GetString(14),
            LastRefreshAttemptRunId =
                reader.IsDBNull(15) ? null : reader.GetInt64(15),
            LastRefreshError =
                reader.IsDBNull(16) ? null : reader.GetString(16),
            RefreshLeaseOwner =
                reader.IsDBNull(17) ? null : reader.GetString(17),
            RefreshLeaseUntilUtc =
                reader.IsDBNull(18) ? null : reader.GetDateTime(18)
        };
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
}
