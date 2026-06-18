using Microsoft.Data.SqlClient;
namespace OpenAlex.Data;
public sealed class ExternalGroupRoleMappingService
{
    private readonly SqlConnectionFactory _connectionFactory;
    public ExternalGroupRoleMappingService(SqlConnectionFactory connectionFactory)=>_connectionFactory=connectionFactory;
    public async Task ApplyMappingsAsync(long appUserId, IReadOnlyList<string> externalGroups, CancellationToken ct=default)
    {
        if (externalGroups.Count==0) return;
        await using var c=_connectionFactory.CreateConnection(); await c.OpenAsync(ct);
        await using var exists=c.CreateCommand(); exists.CommandText="SELECT CASE WHEN OBJECT_ID(N'openalex.AppExternalGroupRoleMapping',N'U') IS NULL THEN 0 ELSE 1 END";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct))==0) return;
        var matched=new HashSet<long>();
        await using (var cmd=c.CreateCommand())
        { cmd.CommandText="SELECT AppRoleId,ExternalGroupValue,MatchType FROM openalex.AppExternalGroupRoleMapping WHERE IsEnabled=1"; await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)){var roleId=r.GetInt64(0); var val=r.GetString(1); var mt=r.GetString(2); foreach(var g in externalGroups) if(IsMatch(g,val,mt)) matched.Add(roleId);} }
        foreach(var roleId in matched){ await using var cmd=c.CreateCommand(); cmd.CommandText="IF NOT EXISTS(SELECT 1 FROM openalex.AppUserRole WHERE AppUserId=@U AND AppRoleId=@R) INSERT INTO openalex.AppUserRole(AppUserId,AppRoleId,CreatedAtUtc) VALUES(@U,@R,SYSUTCDATETIME())"; cmd.Parameters.AddWithValue("@U",appUserId); cmd.Parameters.AddWithValue("@R",roleId); await cmd.ExecuteNonQueryAsync(ct); }
    }
    static bool IsMatch(string group,string value,string type)=> type switch {"Contains"=>group.Contains(value,StringComparison.OrdinalIgnoreCase),"EndsWith"=>group.EndsWith(value,StringComparison.OrdinalIgnoreCase),_=>string.Equals(group,value,StringComparison.OrdinalIgnoreCase)};
}
