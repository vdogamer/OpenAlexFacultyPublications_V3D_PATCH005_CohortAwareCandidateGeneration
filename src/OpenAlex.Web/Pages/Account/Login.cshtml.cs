using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Pages.Account;

public sealed class LoginModel : PageModel
{
    private readonly AuthRepository _authRepository;
    private readonly IOptions<AuthOptions> _authOptions;

    public LoginModel(AuthRepository authRepository, IOptions<AuthOptions> authOptions)
    {
        _authRepository = authRepository;
        _authOptions = authOptions;
    }

    [BindProperty]
    public string? UserName { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (string.Equals(_authOptions.Value.Mode, "ShibbolethHeaders", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "The app is configured for Shibboleth header mode. Local demo login is disabled.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(_authOptions.Value.Mode, "DemoCookie", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Local demo login is disabled because Auth:Mode is not DemoCookie.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            ErrorMessage = "Enter a demo username.";
            return Page();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var profile = await _authRepository.FindUserAsync(UserName, cancellationToken);

        if (profile is null)
        {
            await _authRepository.LogAuthenticationEventAsync(
                UserName,
                null,
                null,
                "DemoCookie",
                false,
                "User not found.",
                ipAddress,
                cancellationToken);

            ErrorMessage = "User was not found. Make sure the V1N auth seed script has been run.";
            return Page();
        }

        if (_authOptions.Value.RequireEnabledUser && !profile.IsEnabled)
        {
            await _authRepository.LogAuthenticationEventAsync(
                profile.UserName,
                profile.DisplayName,
                profile.Email,
                "DemoCookie",
                false,
                "User is disabled.",
                ipAddress,
                cancellationToken);

            ErrorMessage = "User is disabled.";
            return Page();
        }

        var principal = ClaimsPrincipalFactory.Create(profile, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        await _authRepository.UpdateLastLoginAsync(profile.AppUserId, cancellationToken);

        await _authRepository.LogAuthenticationEventAsync(
            profile.UserName,
            profile.DisplayName,
            profile.Email,
            "DemoCookie",
            true,
            null,
            ipAddress,
            cancellationToken);

        return RedirectToPage("/Index");
    }
}
