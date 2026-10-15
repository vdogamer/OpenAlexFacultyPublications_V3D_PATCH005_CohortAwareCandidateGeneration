using System.Security.Claims;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Auth;

public sealed class DepartmentReviewAuthorizationService
{
    private readonly AuthRepository _authRepository;

    public DepartmentReviewAuthorizationService(
        AuthRepository authRepository)
    {
        _authRepository = authRepository;
    }


    public bool IsGlobalAdmin(
        ClaimsPrincipal user)
    {
        return user.IsInRole("Admin");
    }


    public async Task<bool>
        CanReviewDepartmentAsync(
            ClaimsPrincipal user,
            long departmentId,
            CancellationToken cancellationToken = default)
    {
        if (IsGlobalAdmin(user))
        {
            return true;
        }

        if (!user.IsInRole("Reviewer"))
        {
            return false;
        }

        var appUserId =
            GetAppUserId(user);

        if (!appUserId.HasValue)
        {
            return false;
        }

        return await _authRepository
            .UserCanReviewDepartmentAsync(
                appUserId.Value,
                departmentId,
                cancellationToken);
    }


    public async Task<IReadOnlyList<DepartmentAccessItem>>
        GetAllowedDepartmentsAsync(
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
    {
        if (IsGlobalAdmin(user))
        {
            return Array.Empty<DepartmentAccessItem>();
        }

        if (!user.IsInRole("Reviewer"))
        {
            return Array.Empty<DepartmentAccessItem>();
        }

        var appUserId =
            GetAppUserId(user);

        if (!appUserId.HasValue)
        {
            return Array.Empty<DepartmentAccessItem>();
        }

        return await _authRepository
            .GetReviewDepartmentsForUserAsync(
                appUserId.Value,
                cancellationToken);
    }


    private static long? GetAppUserId(
        ClaimsPrincipal user)
    {
        var value =
            user.FindFirstValue(
                ClaimTypes.NameIdentifier);

        return long.TryParse(
            value,
            out var appUserId)
                ? appUserId
                : null;
    }
}