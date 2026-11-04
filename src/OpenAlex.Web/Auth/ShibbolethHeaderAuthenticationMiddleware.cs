using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using OpenAlex.Data;

namespace OpenAlex.Web.Auth;

public sealed class ShibbolethHeaderAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public ShibbolethHeaderAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<AuthOptions> options,
        AuthRepository authRepository,
        ExternalGroupRoleMappingService externalGroupRoleMappingService)
    {
        if (!string.Equals(options.Value.Mode, "ShibbolethHeaders", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        var headerOptions = options.Value.ShibbolethHeaders;
        var userName = ReadHeader(context, headerOptions.UserNameHeader);
        var computingId = ReadHeader(context, headerOptions.ComputingIdHeader);
        var displayName = ReadHeader(context, headerOptions.DisplayNameHeader);
        var email = ReadHeader(context, headerOptions.EmailHeader);
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var groups = ReadDelimitedHeader(context, headerOptions.GroupsHeader, headerOptions.GroupsDelimiter);

        if (string.IsNullOrWhiteSpace(userName))
        {
            await _next(context);
            return;
        }

        try
        {
            var profile = await authRepository.FindUserAsync(userName, context.RequestAborted);

            if (profile is null && options.Value.AllowAutoProvisionUsers)
            {
                profile = await authRepository.EnsureUserAsync(
                    userName,
                    computingId,
                    displayName,
                    email,
                    options.Value.DefaultRoleForNewUsers,
                    context.RequestAborted);
            }

            if (profile is null)
            {
                await authRepository.LogAuthenticationEventAsync(
                    userName,
                    displayName,
                    email,
                    "ShibbolethHeaders",
                    false,
                    "User not found and auto-provisioning is disabled.",
                    ipAddress,
                    context.RequestAborted);

                await _next(context);
                return;
            }

            // Do not recreate a revoked cookie from trusted headers for a disabled account.
            if (!profile.IsEnabled)
            {
                await authRepository.LogAuthenticationEventAsync(
                    userName,
                    displayName,
                    email,
                    "ShibbolethHeaders",
                    false,
                    "User is disabled.",
                    ipAddress,
                    context.RequestAborted);

                await _next(context);
                return;
            }

            await externalGroupRoleMappingService.ApplyMappingsAsync(profile.AppUserId, groups, context.RequestAborted);
            profile = await authRepository.FindUserAsync(profile.UserName, context.RequestAborted) ?? profile;

            var principal = ClaimsPrincipalFactory.Create(profile, CookieAuthenticationDefaults.AuthenticationScheme);
            context.User = principal;
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            await authRepository.UpdateLastLoginAsync(profile.AppUserId, context.RequestAborted);

            await authRepository.LogAuthenticationEventAsync(
                profile.UserName,
                profile.DisplayName,
                profile.Email,
                "ShibbolethHeaders",
                true,
                null,
                ipAddress,
                context.RequestAborted);
        }
        catch (Exception ex)
        {
            await authRepository.LogAuthenticationEventAsync(
                userName,
                displayName,
                email,
                "ShibbolethHeaders",
                false,
                ex.Message,
                ipAddress,
                CancellationToken.None);
        }

        await _next(context);
    }

    private static IReadOnlyList<string> ReadDelimitedHeader(HttpContext context, string headerName, string delimiter)
    {
        var raw = ReadHeader(context, headerName);
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return string.IsNullOrEmpty(delimiter) ? new[] { raw } : raw.Split(delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? ReadHeader(HttpContext context, string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return null;
        }

        return context.Request.Headers.TryGetValue(headerName, out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
