using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Faculty;

[Authorize(Policy = "ReaderPolicy")]
public sealed class PhotoModel : PageModel
{
    private readonly FacultyPhotoService _service;
    private readonly ILogger<PhotoModel> _logger;

    public PhotoModel(FacultyPhotoService service, ILogger<PhotoModel> logger)
    { _service = service; _logger = logger; }

    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    public string? Message { get; private set; }
    public string? ProfileUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserId))
        { Message = "Enter a faculty ComputingID to look for a UVA photo."; return Page(); }
        try
        {
            var result = await _service.LookupAsync(UserId, cancellationToken);
            if (result.Status == FacultyPhotoStatus.Found && result.PhotoUrl is not null)
                return Redirect(result.PhotoUrl);
            ProfileUrl = result.ProfileUrl;
            Message = result.Status switch
            {
                FacultyPhotoStatus.FacultyNotFound => "No faculty directory record matches this ComputingID.",
                FacultyPhotoStatus.IdentityConflict => "Several directory identities match this ComputingID. Review the faculty record before selecting a photo.",
                FacultyPhotoStatus.ProviderUnavailable => "The UVA photo source could not be reached or checked. Please try again later.",
                _ => "No photo link could be confirmed from the available UVA directory information. This does not establish that the faculty member has no photo."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        { Message = "Enter a valid UVA ComputingID or its @virginia.edu email address."; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Faculty photo lookup failed.");
            Message = "The faculty photo lookup could not be completed. Please try again or contact the application administrator.";
        }
        return Page();
    }
}
