using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AuthRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AuthRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AppUserRoleProfile?> FindUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AppUser", "U", cancellationToken))
        {
            return null;
        }

        var hasComputingId = await ColumnExistsAsync(connection, "openalex.AppUser", "ComputingId", cancellationToken);

        var whereClause = hasComputingId
            ? "WHERE u.UserName = @UserName OR u.ComputingId = @UserName OR u.Email = @UserName"
            : "WHERE u.UserName = @UserName OR u.Email = @UserName";

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP 1
    u.AppUserId,
    u.UserName,
    ComputingId = {(hasComputingId ? "u.ComputingId" : "CAST(NULL AS NVARCHAR(256))")},
    u.DisplayName,
    u.Email,
    u.IsEnabled
FROM openalex.AppUser u
{whereClause};";
        command.Parameters.AddWithValue("@UserName", userName);

        AppUserRoleProfile? profile = null;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                profile = new AppUserRoleProfile
                {
                    AppUserId = reader.GetInt64(0),
                    UserName = reader.GetString(1),
                    ComputingId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Email = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsEnabled = reader.GetBoolean(5)
                };
            }
        }

        if (profile is null)
        {
            return null;
        }

        profile.Roles = await GetRolesForUserAsync(connection, profile.AppUserId, cancellationToken);
        return profile;
    }

    public async Task<AppUserRoleProfile> EnsureUserAsync(
        string userName,
        string? computingId,
        string? displayName,
        string? email,
        string defaultRole,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindUserAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var hasComputingId = await ColumnExistsAsync(connection, "openalex.AppUser", "ComputingId", cancellationToken);

        try
        {
            long appUserId;

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;

                if (hasComputingId)
                {
                    insert.CommandText = @"
INSERT INTO openalex.AppUser
(
    UserName,
    ComputingId,
    DisplayName,
    Email,
    IsEnabled,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AppUserId
VALUES
(
    @UserName,
    @ComputingId,
    @DisplayName,
    @Email,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
                    insert.Parameters.AddWithValue("@ComputingId", string.IsNullOrWhiteSpace(computingId) ? DBNull.Value : computingId);
                }
                else
                {
                    insert.CommandText = @"
INSERT INTO openalex.AppUser
(
    UserName,
    DisplayName,
    Email,
    IsEnabled,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AppUserId
VALUES
(
    @UserName,
    @DisplayName,
    @Email,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
                }

                insert.Parameters.AddWithValue("@UserName", userName);
                insert.Parameters.AddWithValue("@DisplayName", string.IsNullOrWhiteSpace(displayName) ? DBNull.Value : displayName);
                insert.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email);

                appUserId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            await using (var role = connection.CreateCommand())
            {
                role.Transaction = transaction;
                role.CommandText = @"
DECLARE @AppRoleId BIGINT;

SELECT @AppRoleId = AppRoleId
FROM openalex.AppRole
WHERE RoleName = @RoleName;

IF @AppRoleId IS NOT NULL
BEGIN
    INSERT INTO openalex.AppUserRole
    (
        AppUserId,
        AppRoleId,
        CreatedAtUtc
    )
    VALUES
    (
        @AppUserId,
        @AppRoleId,
        SYSUTCDATETIME()
    );
END;";
                role.Parameters.AddWithValue("@AppUserId", appUserId);
                role.Parameters.AddWithValue("@RoleName", defaultRole);
                await role.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await FindUserAsync(userName, cancellationToken)
            ?? throw new InvalidOperationException("Failed to load auto-provisioned user.");
    }

    public async Task UpdateLastLoginAsync(long appUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var hasLastLogin = await ColumnExistsAsync(connection, "openalex.AppUser", "LastLoginAtUtc", cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = hasLastLogin
            ? @"
UPDATE openalex.AppUser
SET
    LastLoginAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AppUserId = @AppUserId;"
            : @"
UPDATE openalex.AppUser
SET
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AppUserId = @AppUserId;";

        command.Parameters.AddWithValue("@AppUserId", appUserId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task LogAuthenticationEventAsync(
        string? userName,
        string? displayName,
        string? email,
        string sourceSystem,
        bool succeeded,
        string? failureReason,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AuthenticationEvent", "U", cancellationToken))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AuthenticationEvent
(
    UserName,
    DisplayName,
    Email,
    SourceSystem,
    AuthenticationSucceeded,
    FailureReason,
    IpAddress,
    CreatedAtUtc
)
VALUES
(
    @UserName,
    @DisplayName,
    @Email,
    @SourceSystem,
    @AuthenticationSucceeded,
    @FailureReason,
    @IpAddress,
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@UserName", string.IsNullOrWhiteSpace(userName) ? DBNull.Value : userName);
        command.Parameters.AddWithValue("@DisplayName", string.IsNullOrWhiteSpace(displayName) ? DBNull.Value : displayName);
        command.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email);
        command.Parameters.AddWithValue("@SourceSystem", sourceSystem);
        command.Parameters.AddWithValue("@AuthenticationSucceeded", succeeded);
        command.Parameters.AddWithValue("@FailureReason", string.IsNullOrWhiteSpace(failureReason) ? DBNull.Value : failureReason);
        command.Parameters.AddWithValue("@IpAddress", string.IsNullOrWhiteSpace(ipAddress) ? DBNull.Value : ipAddress);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppUserListItem>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<AppUserListItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AppUser", "U", cancellationToken))
        {
            return rows;
        }

        var hasComputingId = await ColumnExistsAsync(connection, "openalex.AppUser", "ComputingId", cancellationToken);
        var hasLastLogin = await ColumnExistsAsync(connection, "openalex.AppUser", "LastLoginAtUtc", cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT
    u.AppUserId,
    u.UserName,
    ComputingId = {(hasComputingId ? "u.ComputingId" : "CAST(NULL AS NVARCHAR(256))")},
    u.DisplayName,
    u.Email,
    u.IsEnabled,
    u.CreatedAtUtc,
    LastLoginAtUtc = {(hasLastLogin ? "u.LastLoginAtUtc" : "CAST(NULL AS DATETIME2)")},
    RoleSummary = STUFF
    (
        (
            SELECT ', ' + r.RoleName
            FROM openalex.AppUserRole ur
            JOIN openalex.AppRole r
                ON ur.AppRoleId = r.AppRoleId
            WHERE ur.AppUserId = u.AppUserId
            ORDER BY r.RoleName
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'),
        1,
        2,
        ''
    )
FROM openalex.AppUser u
ORDER BY u.UserName;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AppUserListItem
            {
                AppUserId = reader.GetInt64(0),
                UserName = reader.GetString(1),
                ComputingId = reader.IsDBNull(2) ? null : reader.GetString(2),
                DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Email = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsEnabled = reader.GetBoolean(5),
                CreatedAtUtc = reader.GetDateTime(6),
                LastLoginAtUtc = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                RoleSummary = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<AuthenticationEventListItem>> GetAuthenticationEventsAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        var rows = new List<AuthenticationEventListItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AuthenticationEvent", "U", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    AuthenticationEventId,
    UserName,
    DisplayName,
    Email,
    SourceSystem,
    AuthenticationSucceeded,
    FailureReason,
    IpAddress,
    CreatedAtUtc
FROM openalex.AuthenticationEvent
ORDER BY AuthenticationEventId DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AuthenticationEventListItem
            {
                AuthenticationEventId = reader.GetInt64(0),
                UserName = reader.IsDBNull(1) ? null : reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Email = reader.IsDBNull(3) ? null : reader.GetString(3),
                SourceSystem = reader.GetString(4),
                AuthenticationSucceeded = reader.GetBoolean(5),
                FailureReason = reader.IsDBNull(6) ? null : reader.GetString(6),
                IpAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAtUtc = reader.GetDateTime(8)
            });
        }

        return rows;
    }

    private static async Task<IReadOnlyList<string>> GetRolesForUserAsync(
        SqlConnection connection,
        long appUserId,
        CancellationToken cancellationToken)
    {
        var roles = new List<string>();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT r.RoleName
FROM openalex.AppUserRole ur
JOIN openalex.AppRole r
    ON ur.AppRoleId = r.AppRoleId
WHERE ur.AppUserId = @AppUserId
ORDER BY r.RoleName;";
        command.Parameters.AddWithValue("@AppUserId", appUserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(reader.GetString(0));
        }

        return roles;
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string objectName, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN COL_LENGTH(@ObjectName, @ColumnName) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
