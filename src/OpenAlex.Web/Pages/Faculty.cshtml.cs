using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages;

[Authorize(Policy = "ReaderPolicy")]
public sealed class FacultyModel : PageModel
{
    private readonly IFacultyDirectoryRepository _repository;
    private readonly ILogger<FacultyModel> _logger;

    public FacultyModel(IFacultyDirectoryRepository repository, ILogger<FacultyModel> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public string? Search { get; private set; }
    public FacultyDirectoryPage? Result { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string? search, int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        Search = search?.Trim();
        if (Search?.Length > 200) Search = Search[..200];
        try
        {
            Result = await _repository.GetPageAsync(Search, pageNumber, 100, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Faculty directory could not be loaded.");
            ErrorMessage = "Faculty records could not be loaded. Please try again or contact the application administrator.";
        }
    }
}
