using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class FacultyDoisModel : PageModel
{
    private readonly FacultyDoiLookupService _service;
    private readonly ILogger<FacultyDoisModel> _logger;

    public FacultyDoisModel(FacultyDoiLookupService service, ILogger<FacultyDoisModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    public FacultyDoiLookupResult? Result { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (UserId is null) return;

        try
        {
            UserId = AcademicOnlineUniversalFacultyLookupService.NormalizeUvaUserId(UserId);
        }
        catch (InvalidOperationException)
        {
            ErrorMessage = "Enter a valid userid/ComputingID (2–32 letters or numbers), or its @virginia.edu email address.";
            return;
        }

        try
        {
            Result = await _service.LookupAsync(UserId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Faculty DOI lookup failed.");
            ErrorMessage = "Publication data could not be loaded. Please try again or contact the application administrator.";
        }
    }
}
