using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class RoleManagementRepository
{
    private readonly SqlConnectionFactory _connectionFactory;
    public RoleManagementRepository(SqlConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<RoleManagementUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = new Dictionary<long, RoleManagementUser>();
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT u.AppUserId,u.UserName,u.DisplayName,u.Email,u.IsEnabled,r.RoleName
FROM openalex.AppUser u
LEFT JOIN openalex.AppUserRole ur ON u.AppUserId=ur.AppUserId
LEFT JOIN openalex.AppRole r ON ur.AppRoleId=r.AppRoleId
ORDER BY u.UserName,r.RoleName;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            if (!users.TryGetValue(id, out var user))
            {
                user = new RoleManagementUser{ AppUserId=id, UserName=reader.GetString(1), DisplayName=reader.IsDBNull(2)?null:reader.GetString(2), Email=reader.IsDBNull(3)?null:reader.GetString(3), IsEnabled=reader.GetBoolean(4), Roles=new List<string>() };
                users.Add(id,user);
            }
            if (!reader.IsDBNull(5)) ((List<string>)user.Roles).Add(reader.GetString(5));
        }
        return users.Values.OrderBy(u=>u.UserName).ToList();
    }

    public async Task<RoleManagementUser?> GetUserAsync(long appUserId, CancellationToken cancellationToken = default) => (await GetUsersAsync(cancellationToken)).FirstOrDefault(u=>u.AppUserId==appUserId);

    public async Task<IReadOnlyList<RoleDefinition>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = new List<RoleDefinition>();
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AppRoleId, RoleName FROM openalex.AppRole ORDER BY RoleName;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) roles.Add(new RoleDefinition{ AppRoleId=reader.GetInt64(0), RoleName=reader.GetString(1)});
        return roles;
    }

    public async Task SaveUserRolesAsync(long appUserId, IReadOnlyList<long> selectedRoleIds, string changedBy, string? changeReason, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var userName = await ScalarStringAsync(connection, tx, "SELECT UserName FROM openalex.AppUser WHERE AppUserId=@Id", appUserId, cancellationToken) ?? throw new InvalidOperationException("User was not found.");
            var roleLookup = await GetRoleLookupAsync(connection, tx, cancellationToken);
            var existing = await GetUserRoleIdsAsync(connection, tx, appUserId, cancellationToken);
            var selected = selectedRoleIds.Distinct().ToHashSet();
            var adminRoleId = roleLookup.FirstOrDefault(x=>string.Equals(x.Value,"Admin",StringComparison.OrdinalIgnoreCase)).Key;
            if (adminRoleId>0 && existing.Contains(adminRoleId) && !selected.Contains(adminRoleId) && await CountEnabledAdminsAsync(connection,tx,cancellationToken)<=1)
                throw new InvalidOperationException("Cannot remove the Admin role from the last enabled Admin user.");
            foreach (var roleId in existing.Except(selected))
            {
                await ExecAsync(connection,tx,"DELETE FROM openalex.AppUserRole WHERE AppUserId=@A AND AppRoleId=@B", appUserId, roleId, cancellationToken);
                await AuditAsync(connection,tx,appUserId,userName,changedBy,"RoleRemoved",roleLookup.GetValueOrDefault(roleId),"Assigned","Removed",changeReason,cancellationToken);
            }
            foreach (var roleId in selected.Except(existing))
            {
                await ExecAsync(connection,tx,"IF NOT EXISTS (SELECT 1 FROM openalex.AppUserRole WHERE AppUserId=@A AND AppRoleId=@B) INSERT INTO openalex.AppUserRole(AppUserId,AppRoleId,CreatedAtUtc) VALUES(@A,@B,SYSUTCDATETIME())", appUserId, roleId, cancellationToken);
                await AuditAsync(connection,tx,appUserId,userName,changedBy,"RoleAdded",roleLookup.GetValueOrDefault(roleId),"NotAssigned","Assigned",changeReason,cancellationToken);
            }
            await ExecOneAsync(connection,tx,"UPDATE openalex.AppUser SET UpdatedAtUtc=SYSUTCDATETIME() WHERE AppUserId=@Id", appUserId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        } catch { await tx.RollbackAsync(cancellationToken); throw; }
    }

    public async Task SetUserEnabledAsync(long appUserId, bool isEnabled, string changedBy, string? reason, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var userName = await ScalarStringAsync(connection, tx, "SELECT UserName FROM openalex.AppUser WHERE AppUserId=@Id", appUserId, cancellationToken) ?? throw new InvalidOperationException("User was not found.");
            var roleLookup = await GetRoleLookupAsync(connection, tx, cancellationToken);
            var existing = await GetUserRoleIdsAsync(connection, tx, appUserId, cancellationToken);
            var adminRoleId = roleLookup.FirstOrDefault(x=>string.Equals(x.Value,"Admin",StringComparison.OrdinalIgnoreCase)).Key;
            if (!isEnabled && adminRoleId>0 && existing.Contains(adminRoleId) && await CountEnabledAdminsAsync(connection,tx,cancellationToken)<=1)
                throw new InvalidOperationException("Cannot disable the last enabled Admin user.");
            await ExecOneAsync(connection,tx,"UPDATE openalex.AppUser SET IsEnabled=@Value, UpdatedAtUtc=SYSUTCDATETIME() WHERE AppUserId=@Id", appUserId, isEnabled, cancellationToken);
            await AuditAsync(connection,tx,appUserId,userName,changedBy,isEnabled?"UserEnabled":"UserDisabled",null,isEnabled?"Disabled":"Enabled",isEnabled?"Enabled":"Disabled",reason,cancellationToken);
            await tx.CommitAsync(cancellationToken);
        } catch { await tx.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<IReadOnlyList<ExternalGroupRoleMapping>> GetExternalGroupMappingsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<ExternalGroupRoleMapping>();
        await using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(cancellationToken);
        if (!await ObjectExistsAsync(connection,"openalex.AppExternalGroupRoleMapping","U",cancellationToken)) return rows;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AppExternalGroupRoleMappingId,ExternalGroupValue,MatchType,AppRoleId,RoleName,IsEnabled,MappingDescription FROM openalex.AppExternalGroupRoleMapping ORDER BY RoleName,ExternalGroupValue;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new ExternalGroupRoleMapping{AppExternalGroupRoleMappingId=reader.GetInt64(0),ExternalGroupValue=reader.GetString(1),MatchType=reader.GetString(2),AppRoleId=reader.GetInt64(3),RoleName=reader.GetString(4),IsEnabled=reader.GetBoolean(5),MappingDescription=reader.IsDBNull(6)?null:reader.GetString(6)});
        return rows;
    }

    public async Task<IReadOnlyList<DepartmentReviewerAdminUser>>
    GetDepartmentReviewerUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var rows =
            new List<DepartmentReviewerAdminUser>();

        var users =
            await GetUsersAsync(cancellationToken);

        foreach (var user in users)
        {
            var isReviewer =
                user.Roles.Any(
                    r => string.Equals(
                        r,
                        "Reviewer",
                        StringComparison.OrdinalIgnoreCase));

            var isAdmin =
                user.Roles.Any(
                    r => string.Equals(
                        r,
                        "Admin",
                        StringComparison.OrdinalIgnoreCase));

            if (!isReviewer && !isAdmin)
            {
                continue;
            }

            rows.Add(
                new DepartmentReviewerAdminUser
                {
                    AppUserId = user.AppUserId,
                    UserName = user.UserName,
                    DisplayName = user.DisplayName,
                    Email = user.Email,
                    IsEnabled = user.IsEnabled,
                    IsReviewer = isReviewer,
                    IsAdmin = isAdmin,
                    RoleSummary = user.RoleSummary
                });
        }

        return rows;
    }


    public async Task<IReadOnlyList<DepartmentReviewerOption>>
        GetDepartmentReviewerOptionsAsync(
            long appUserId,
            CancellationToken cancellationToken = default)
    {
        var rows =
            new List<DepartmentReviewerOption>();

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
    WITH Departments AS
    (
        SELECT
            DepartmentId,
            DepartmentName = MAX(DepartmentName)

        FROM academiconline.vw_FacultyPublicationReviewUniverse

        GROUP BY DepartmentId
    )
    SELECT
        d.DepartmentId,
        d.DepartmentName,

        IsAssigned =
            CONVERT(
                bit,
                CASE
                    WHEN a.AppUserDepartmentAccessId IS NOT NULL
                    AND a.IsActive = 1
                    AND a.CanReview = 1
                        THEN 1
                    ELSE 0
                END
            )

    FROM Departments d

    LEFT JOIN openalex.AppUserDepartmentAccess a
        ON a.DepartmentId = d.DepartmentId
    AND a.AppUserId = @AppUserId

    ORDER BY d.DepartmentName;";

        command.Parameters.AddWithValue(
            "@AppUserId",
            appUserId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                cancellationToken))
        {
            rows.Add(
                new DepartmentReviewerOption
                {
                    DepartmentId =
                        reader.GetInt64(0),

                    DepartmentName =
                        reader.GetString(1),

                    IsAssigned =
                        reader.GetBoolean(2)
                });
        }

        return rows;
    }

    public async Task<ExternalGroupRoleMapping?> GetExternalGroupMappingAsync(long id, CancellationToken ct=default) => (await GetExternalGroupMappingsAsync(ct)).FirstOrDefault(m=>m.AppExternalGroupRoleMappingId==id);

    public async Task SaveExternalGroupMappingAsync(long mappingId, string externalGroupValue, string matchType, long appRoleId, bool isEnabled, string? description, string changedBy, CancellationToken ct=default)
    {
        var role = (await GetRolesAsync(ct)).FirstOrDefault(r=>r.AppRoleId==appRoleId) ?? throw new InvalidOperationException("Selected role was not found.");
        await using var connection = _connectionFactory.CreateConnection(); await connection.OpenAsync(ct); await using var command = connection.CreateCommand();
        command.CommandText = mappingId>0 ? @"UPDATE openalex.AppExternalGroupRoleMapping SET ExternalGroupValue=@G,MatchType=@M,AppRoleId=@R,RoleName=@RoleName,IsEnabled=@E,MappingDescription=@D,UpdatedBy=@By,UpdatedAtUtc=SYSUTCDATETIME() WHERE AppExternalGroupRoleMappingId=@Id" : @"INSERT INTO openalex.AppExternalGroupRoleMapping(ExternalGroupValue,MatchType,AppRoleId,RoleName,IsEnabled,MappingDescription,CreatedBy,CreatedAtUtc,UpdatedBy,UpdatedAtUtc) VALUES(@G,@M,@R,@RoleName,@E,@D,@By,SYSUTCDATETIME(),@By,SYSUTCDATETIME())";
        if (mappingId>0) command.Parameters.AddWithValue("@Id",mappingId);
        command.Parameters.AddWithValue("@G",externalGroupValue); command.Parameters.AddWithValue("@M",matchType); command.Parameters.AddWithValue("@R",appRoleId); command.Parameters.AddWithValue("@RoleName",role.RoleName); command.Parameters.AddWithValue("@E",isEnabled); command.Parameters.AddWithValue("@D",string.IsNullOrWhiteSpace(description)?DBNull.Value:description); command.Parameters.AddWithValue("@By",changedBy);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveDepartmentReviewerAccessAsync(
    long appUserId,
    IReadOnlyCollection<long> selectedDepartmentIds,
    string changedBy,
    string? changeReason,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            string? userName;

            await using
                (var userCommand =
                    connection.CreateCommand())
            {
                userCommand.Transaction =
                    transaction;

                userCommand.CommandText = @"
    SELECT UserName
    FROM openalex.AppUser
    WHERE AppUserId = @AppUserId;";

                userCommand.Parameters.AddWithValue(
                    "@AppUserId",
                    appUserId);

                userName =
                    Convert.ToString(
                        await userCommand
                            .ExecuteScalarAsync(
                                cancellationToken));
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new InvalidOperationException(
                    "User was not found.");
            }

            var selected =
                selectedDepartmentIds
                    .Distinct()
                    .ToHashSet();

            var existing =
                new Dictionary<long, (bool CanReview, bool IsActive)>();

            await using
                (var existingCommand =
                    connection.CreateCommand())
            {
                existingCommand.Transaction =
                    transaction;

                existingCommand.CommandText = @"
    SELECT
        DepartmentId,
        CanReview,
        IsActive
    FROM openalex.AppUserDepartmentAccess
    WHERE AppUserId = @AppUserId;";

                existingCommand.Parameters.AddWithValue(
                    "@AppUserId",
                    appUserId);

                await using var reader =
                    await existingCommand
                        .ExecuteReaderAsync(
                            cancellationToken);

                while (await reader.ReadAsync(
                        cancellationToken))
                {
                    existing[
                        reader.GetInt64(0)] =
                        (
                            reader.GetBoolean(1),
                            reader.GetBoolean(2)
                        );
                }
            }


            foreach (var departmentId in selected)
            {
                existing.TryGetValue(
                    departmentId,
                    out var oldState);

                await using var command =
                    connection.CreateCommand();

                command.Transaction =
                    transaction;

                command.CommandText = @"
    IF EXISTS
    (
        SELECT 1
        FROM openalex.AppUserDepartmentAccess
        WHERE AppUserId = @AppUserId
        AND DepartmentId = @DepartmentId
    )
    BEGIN

        UPDATE openalex.AppUserDepartmentAccess
        SET
            CanReview = 1,
            IsActive = 1,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE AppUserId = @AppUserId
        AND DepartmentId = @DepartmentId;

    END
    ELSE
    BEGIN

        INSERT INTO openalex.AppUserDepartmentAccess
        (
            AppUserId,
            DepartmentId,
            CanReview,
            IsActive,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            @AppUserId,
            @DepartmentId,
            1,
            1,
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );

    END;";

                command.Parameters.AddWithValue(
                    "@AppUserId",
                    appUserId);

                command.Parameters.AddWithValue(
                    "@DepartmentId",
                    departmentId);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);


                if (!oldState.IsActive
                    || !oldState.CanReview)
                {
                    await InsertDepartmentAccessAuditAsync(
                        connection,
                        transaction,
                        appUserId,
                        userName,
                        departmentId,
                        changedBy,
                        "DepartmentAccessGranted",
                        oldState.CanReview,
                        true,
                        oldState.IsActive,
                        true,
                        changeReason,
                        cancellationToken);
                }
            }


            foreach (var pair in existing)
            {
                if (!pair.Value.IsActive
                    || !pair.Value.CanReview
                    || selected.Contains(pair.Key))
                {
                    continue;
                }

                await using var command =
                    connection.CreateCommand();

                command.Transaction =
                    transaction;

                command.CommandText = @"
    UPDATE openalex.AppUserDepartmentAccess
    SET
        CanReview = 0,
        IsActive = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE AppUserId = @AppUserId
    AND DepartmentId = @DepartmentId;";

                command.Parameters.AddWithValue(
                    "@AppUserId",
                    appUserId);

                command.Parameters.AddWithValue(
                    "@DepartmentId",
                    pair.Key);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);

                await InsertDepartmentAccessAuditAsync(
                    connection,
                    transaction,
                    appUserId,
                    userName,
                    pair.Key,
                    changedBy,
                    "DepartmentAccessRemoved",
                    pair.Value.CanReview,
                    false,
                    pair.Value.IsActive,
                    false,
                    changeReason,
                    cancellationToken);
            }


            await transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    public async Task<IReadOnlyList<DepartmentReviewerAccessAuditItem>>
    GetDepartmentReviewerAccessAuditAsync(
        long? appUserId = null,
        int top = 200,
        CancellationToken cancellationToken = default)
        {
            var rows =
                new List<DepartmentReviewerAccessAuditItem>();

            await using var connection =
                _connectionFactory.CreateConnection();

            await connection.OpenAsync(cancellationToken);

            if (!await ObjectExistsAsync(
                    connection,
                    "openalex.AppUserDepartmentAccessAudit",
                    "U",
                    cancellationToken))
            {
                return rows;
            }

            await using var command =
                connection.CreateCommand();

            var safeTop =
                Math.Clamp(top, 1, 1000);

            command.CommandText = $@"
        SELECT TOP ({safeTop})
            AppUserDepartmentAccessAuditId,
            TargetAppUserId,
            TargetUserName,

            DepartmentId,
            DepartmentName,

            ChangedBy,
            ChangeType,

            OldCanReview,
            NewCanReview,

            OldIsActive,
            NewIsActive,

            ChangeReason,
            CreatedAtUtc

        FROM openalex.AppUserDepartmentAccessAudit

        WHERE
            @AppUserId IS NULL
            OR TargetAppUserId = @AppUserId

        ORDER BY
            CreatedAtUtc DESC,
            AppUserDepartmentAccessAuditId DESC;";

            command.Parameters.AddWithValue(
                "@AppUserId",
                appUserId.HasValue
                    ? appUserId.Value
                    : DBNull.Value);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                    cancellationToken))
            {
                rows.Add(
                    new DepartmentReviewerAccessAuditItem
                    {
                        AuditId =
                            reader.GetInt64(0),

                        TargetAppUserId =
                            reader.GetInt64(1),

                        TargetUserName =
                            reader.GetString(2),

                        DepartmentId =
                            reader.GetInt64(3),

                        DepartmentName =
                            reader.IsDBNull(4)
                                ? null
                                : reader.GetString(4),

                        ChangedBy =
                            reader.GetString(5),

                        ChangeType =
                            reader.GetString(6),

                        OldCanReview =
                            reader.IsDBNull(7)
                                ? null
                                : reader.GetBoolean(7),

                        NewCanReview =
                            reader.IsDBNull(8)
                                ? null
                                : reader.GetBoolean(8),

                        OldIsActive =
                            reader.IsDBNull(9)
                                ? null
                                : reader.GetBoolean(9),

                        NewIsActive =
                            reader.IsDBNull(10)
                                ? null
                                : reader.GetBoolean(10),

                        ChangeReason =
                            reader.IsDBNull(11)
                                ? null
                                : reader.GetString(11),

                        CreatedAtUtc =
                            reader.GetDateTime(12)
                    });
            }

            return rows;
        }

    private static async Task
    InsertDepartmentAccessAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long appUserId,
        string userName,
        long departmentId,
        string changedBy,
        string changeType,
        bool oldCanReview,
        bool newCanReview,
        bool oldIsActive,
        bool newIsActive,
        string? reason,
        CancellationToken cancellationToken)
{
    await using var command =
        connection.CreateCommand();

    command.Transaction =
        transaction;

    command.CommandText = @"
INSERT INTO openalex.AppUserDepartmentAccessAudit
(
    TargetAppUserId,
    TargetUserName,

    DepartmentId,
    DepartmentName,

    ChangedBy,
    ChangeType,

    OldCanReview,
    NewCanReview,

    OldIsActive,
    NewIsActive,

    ChangeReason,
    CreatedAtUtc
)
SELECT
    @AppUserId,
    @UserName,

    @DepartmentId,
    MAX(s.DepartmentName),

    @ChangedBy,
    @ChangeType,

    @OldCanReview,
    @NewCanReview,

    @OldIsActive,
    @NewIsActive,

    @ChangeReason,
    SYSUTCDATETIME()

FROM academiconline.vw_FacultyPublicationReviewUniverse s

WHERE s.DepartmentId =
      @DepartmentId;";

    command.Parameters.AddWithValue(
        "@AppUserId",
        appUserId);

    command.Parameters.AddWithValue(
        "@UserName",
        userName);

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    command.Parameters.AddWithValue(
        "@ChangedBy",
        changedBy);

    command.Parameters.AddWithValue(
        "@ChangeType",
        changeType);

    command.Parameters.AddWithValue(
        "@OldCanReview",
        oldCanReview);

    command.Parameters.AddWithValue(
        "@NewCanReview",
        newCanReview);

    command.Parameters.AddWithValue(
        "@OldIsActive",
        oldIsActive);

    command.Parameters.AddWithValue(
        "@NewIsActive",
        newIsActive);

    command.Parameters.AddWithValue(
        "@ChangeReason",
        string.IsNullOrWhiteSpace(reason)
            ? DBNull.Value
            : reason);

    await command.ExecuteNonQueryAsync(
        cancellationToken);
}

    static async Task<HashSet<long>> GetUserRoleIdsAsync(SqlConnection c, SqlTransaction t, long id, CancellationToken ct){var s=new HashSet<long>(); await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT AppRoleId FROM openalex.AppUserRole WHERE AppUserId=@Id"; cmd.Parameters.AddWithValue("@Id",id); await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) s.Add(r.GetInt64(0)); return s;}
    static async Task<Dictionary<long,string>> GetRoleLookupAsync(SqlConnection c, SqlTransaction t, CancellationToken ct){var d=new Dictionary<long,string>(); await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT AppRoleId,RoleName FROM openalex.AppRole"; await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) d[r.GetInt64(0)]=r.GetString(1); return d;}
    static async Task<long> CountEnabledAdminsAsync(SqlConnection c, SqlTransaction t, CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT COUNT_BIG(*) FROM openalex.AppUser u JOIN openalex.AppUserRole ur ON u.AppUserId=ur.AppUserId JOIN openalex.AppRole r ON ur.AppRoleId=r.AppRoleId WHERE u.IsEnabled=1 AND r.RoleName='Admin'"; return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));}
    static async Task<string?> ScalarStringAsync(SqlConnection c, SqlTransaction t, string sql, long id, CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; cmd.Parameters.AddWithValue("@Id",id); return Convert.ToString(await cmd.ExecuteScalarAsync(ct));}
    static async Task ExecOneAsync(SqlConnection c, SqlTransaction t, string sql, long id, CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; cmd.Parameters.AddWithValue("@Id",id); await cmd.ExecuteNonQueryAsync(ct);}    
    static async Task ExecOneAsync(SqlConnection c, SqlTransaction t, string sql, long id, bool val, CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; cmd.Parameters.AddWithValue("@Id",id); cmd.Parameters.AddWithValue("@Value",val); await cmd.ExecuteNonQueryAsync(ct);}    
    static async Task ExecAsync(SqlConnection c, SqlTransaction t, string sql, long a, long b, CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; cmd.Parameters.AddWithValue("@A",a); cmd.Parameters.AddWithValue("@B",b); await cmd.ExecuteNonQueryAsync(ct);}    
    static async Task AuditAsync(SqlConnection c, SqlTransaction t, long userId, string userName, string by, string type, string? role, string? oldVal, string? newVal, string? reason, CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="IF OBJECT_ID(N'openalex.AppRoleChangeAudit',N'U') IS NOT NULL INSERT INTO openalex.AppRoleChangeAudit(TargetAppUserId,TargetUserName,ChangedBy,ChangeType,RoleName,OldValue,NewValue,ChangeReason,CreatedAtUtc) VALUES(@U,@UN,@By,@T,@R,@O,@N,@Reason,SYSUTCDATETIME())"; cmd.Parameters.AddWithValue("@U",userId); cmd.Parameters.AddWithValue("@UN",userName); cmd.Parameters.AddWithValue("@By",by); cmd.Parameters.AddWithValue("@T",type); cmd.Parameters.AddWithValue("@R",string.IsNullOrWhiteSpace(role)?DBNull.Value:role); cmd.Parameters.AddWithValue("@O",string.IsNullOrWhiteSpace(oldVal)?DBNull.Value:oldVal); cmd.Parameters.AddWithValue("@N",string.IsNullOrWhiteSpace(newVal)?DBNull.Value:newVal); cmd.Parameters.AddWithValue("@Reason",string.IsNullOrWhiteSpace(reason)?DBNull.Value:reason); await cmd.ExecuteNonQueryAsync(ct);}    
    static async Task<bool> ObjectExistsAsync(SqlConnection c,string obj,string typ,CancellationToken ct){await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT CASE WHEN OBJECT_ID(@O,@T) IS NULL THEN 0 ELSE 1 END"; cmd.Parameters.AddWithValue("@O",obj); cmd.Parameters.AddWithValue("@T",typ); return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;}
}
