using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class AuthEventsModel : PageModel
{
    private readonly AuthRepository _authRepository;

    public AuthEventsModel(AuthRepository authRepository)
    {
        _authRepository = authRepository;
    }

    public IReadOnlyList<AuthenticationEventListItem> Events { get; private set; } = Array.Empty<AuthenticationEventListItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Events = await _authRepository.GetAuthenticationEventsAsync(200, cancellationToken);
    }
}
