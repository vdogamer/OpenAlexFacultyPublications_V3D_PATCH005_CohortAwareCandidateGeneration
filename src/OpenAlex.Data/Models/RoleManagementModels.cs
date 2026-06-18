namespace OpenAlex.Data.Models;

public sealed class RoleManagementUser
{
    public long AppUserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsEnabled { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public string RoleSummary => Roles.Count == 0 ? string.Empty : string.Join(", ", Roles);
}

public sealed class RoleDefinition { public long AppRoleId { get; set; } public string RoleName { get; set; } = string.Empty; }

public sealed class ExternalGroupRoleMapping
{
    public long AppExternalGroupRoleMappingId { get; set; }
    public string ExternalGroupValue { get; set; } = string.Empty;
    public string MatchType { get; set; } = "Exact";
    public long AppRoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? MappingDescription { get; set; }
}
