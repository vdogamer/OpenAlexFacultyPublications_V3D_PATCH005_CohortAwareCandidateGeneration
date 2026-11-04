using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenAlex.Data;

namespace OpenAlex.Web.Auth;

public sealed class LiveCookieValidationEvents(IDepartmentReviewAuthorizationStore store) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userName = principal?.Identity?.Name;
        var hasId = long.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var appUserId);
        var profile = hasId && appUserId > 0 && !string.IsNullOrWhiteSpace(userName)
            ? await store.FindUserAsync(userName, context.HttpContext.RequestAborted)
            : null;
        var cookieRoles = principal?.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (principal?.Identity?.IsAuthenticated == true
            && profile is { IsEnabled: true } && profile.AppUserId == appUserId
            && string.Equals(profile.UserName, userName, StringComparison.OrdinalIgnoreCase)
            && cookieRoles.SetEquals(profile.Roles))
            return;

        // No validation interval/cache: disabling or changing roles invalidates
        // an already-issued cookie before any Admin/Reviewer policy can use it.
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
