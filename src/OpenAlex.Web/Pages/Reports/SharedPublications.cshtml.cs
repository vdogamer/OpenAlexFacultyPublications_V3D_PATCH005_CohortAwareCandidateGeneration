using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class SharedPublicationsModel : PageModel
{
    private readonly SharedPublicationsService _service;

    public SharedPublicationsModel(
        SharedPublicationsService service)
    {
        _service = service;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserIds { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool VerifiedOnly { get; set; }

    public SharedPublicationsSearchResult? Result { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserIds))
        {
            return;
        }

        var ids = UserIds.Split(
            new[] { ',', ';', '\r', '\n', '\t', ' ' },
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        try
        {
            Result = await _service.SearchAsync(
                ids,
                VerifiedOnly,
                cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
