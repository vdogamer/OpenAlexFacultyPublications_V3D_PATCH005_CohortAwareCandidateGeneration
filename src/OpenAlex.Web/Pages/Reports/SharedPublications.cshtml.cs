using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class SharedPublicationsModel : PageModel
{
    private readonly SharedPublicationsRepository _repository;

    public SharedPublicationsModel(SharedPublicationsRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserIds { get; set; }

    public SharedPublicationSearchResult? Result { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserIds))
        {
            return;
        }

        var ids = UserIds
            .Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        try
        {
            Result = await _repository.FindSharedPublicationsAsync(ids, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
