using System.Security.Claims;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Auth;

public static class ClaimsPrincipalFactory
{
    public static ClaimsPrincipal Create(AppUserRoleProfile profile, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, profile.AppUserId.ToString()),
            new(ClaimTypes.Name, profile.UserName)
        };

        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            claims.Add(new("display_name", profile.DisplayName));
        }

        if (!string.IsNullOrWhiteSpace(profile.Email))
        {
            claims.Add(new(ClaimTypes.Email, profile.Email));
        }

        if (!string.IsNullOrWhiteSpace(profile.ComputingId))
        {
            claims.Add(new("computing_id", profile.ComputingId));
        }

        foreach (var role in profile.Roles)
        {
            claims.Add(new(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, authenticationType);
        return new ClaimsPrincipal(identity);
    }
}
