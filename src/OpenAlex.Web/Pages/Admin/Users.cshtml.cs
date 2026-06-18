using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class UsersModel : PageModel
{
    private readonly AuthRepository _authRepository;

    public UsersModel(AuthRepository authRepository)
    {
        _authRepository = authRepository;
    }

    public IReadOnlyList<AppUserListItem> Users { get; private set; } = Array.Empty<AppUserListItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Users = await _authRepository.GetUsersAsync(cancellationToken);
    }
}
