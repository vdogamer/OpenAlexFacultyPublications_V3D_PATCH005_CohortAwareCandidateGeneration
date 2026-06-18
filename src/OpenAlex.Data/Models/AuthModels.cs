namespace OpenAlex.Data.Models;

public sealed class AppUserRoleProfile
{
    public long AppUserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? ComputingId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsEnabled { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

public sealed class AppUserListItem
{
    public long AppUserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? ComputingId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public string RoleSummary { get; set; } = string.Empty;
}

public sealed class AuthenticationEventListItem
{
    public long AuthenticationEventId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public bool AuthenticationSucceeded { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
