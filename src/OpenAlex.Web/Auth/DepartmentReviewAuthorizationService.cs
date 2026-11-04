using System.Security.Claims;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Auth;

public sealed class DepartmentReviewAuthorizationService
{
    private readonly IDepartmentReviewAuthorizationStore _authRepository;

    public DepartmentReviewAuthorizationService(IDepartmentReviewAuthorizationStore authRepository)
    {
        _authRepository = authRepository;
    }

    public async Task<bool> IsGlobalAdminAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var profile = await GetLiveProfileAsync(user, cancellationToken);
        return profile?.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) == true;
    }

    public async Task<bool> CanReviewDepartmentAsync(
        ClaimsPrincipal user, long departmentId, CancellationToken cancellationToken = default)
    {
        if (departmentId <= 0) return false;
        var profile = await GetLiveProfileAsync(user, cancellationToken);
        if (profile is null) return false;
        if (profile.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase)) return true;
        if (!profile.Roles.Contains("Reviewer", StringComparer.OrdinalIgnoreCase)) return false;

        // Recheck live role/access in SQL too; permissions may change after the profile read.
        return await _authRepository.UserCanReviewDepartmentAsync(profile.AppUserId, departmentId, cancellationToken);
    }

    public async Task<IReadOnlyList<DepartmentAccessItem>> GetAllowedDepartmentsAsync(
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var profile = await GetLiveProfileAsync(user, cancellationToken);
        // Existing Admin contract: an empty list means unrestricted only after
        // the caller has passed the live IsGlobalAdminAsync check.
        if (profile is null || profile.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase)
            || !profile.Roles.Contains("Reviewer", StringComparer.OrdinalIgnoreCase))
            return Array.Empty<DepartmentAccessItem>();

        return await _authRepository.GetReviewDepartmentsForUserAsync(profile.AppUserId, cancellationToken);
    }

    private async Task<AppUserRoleProfile?> GetLiveProfileAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.Identity.Name)
            || !long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var appUserId) || appUserId <= 0)
            return null;

        var profile = await _authRepository.FindUserAsync(user.Identity.Name, cancellationToken);
        // Bind the signed identity to the same current account, not a reused username/email.
        return profile is { IsEnabled: true } && profile.AppUserId == appUserId
            && string.Equals(profile.UserName, user.Identity.Name, StringComparison.OrdinalIgnoreCase)
                ? profile : null;
    }
}
