using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Faculty;

[Authorize(Policy = "ReaderPolicy")]
public sealed class MyPublicationsModel : PageModel
{
    private readonly FacultyAcademicOnlineMappingRepository _mappingRepository;
    private readonly AcademicOnlineFacultyPublicationsService _publicationsService;
    private readonly AcademicOnlinePublicationHtmlFormatter _formatter;

    public MyPublicationsModel(
        FacultyAcademicOnlineMappingRepository mappingRepository,
        AcademicOnlineFacultyPublicationsService publicationsService,
        AcademicOnlinePublicationHtmlFormatter formatter)
    {
        _mappingRepository = mappingRepository;
        _publicationsService = publicationsService;
        _formatter = formatter;
    }

    [BindProperty(SupportsGet = true)]
    public bool VerifiedOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ToYear { get; set; }

    public AcademicOnlineFacultyPublicationsResult? Result { get; private set; }
    public string? GeneratedHtmlFragment { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? SignedInDisplayName { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            ValidateYearRange();

            var email = User.FindFirstValue(ClaimTypes.Email);
            var computingId = User.FindFirstValue("computing_id");
            var userName = User.Identity?.Name;
            SignedInDisplayName =
                User.FindFirstValue("display_name")
                ?? userName
                ?? "Faculty member";

            var mapping = await _mappingRepository.ResolveCurrentMappingForSignedInUserAsync(
                email,
                computingId,
                userName,
                cancellationToken);

            if (mapping is null)
            {
                ErrorMessage =
                    "We could not match your signed-in account to a faculty profile. " +
                    "Please contact the faculty-profile administrator for assistance.";
                return;
            }

            if (!mapping.HasCurrentMapping)
            {
                ErrorMessage =
                    "Your faculty profile was found, but its Academic Online publication mapping " +
                    "has not been verified yet. Please contact the faculty-profile administrator.";
                return;
            }

            Result = await _publicationsService.GetAsync(
                mapping.FacultyId,
                VerifiedOnly,
                FromYear,
                ToYear,
                cancellationToken);

            GeneratedHtmlFragment = _formatter.BuildFragment(Result);
        }
        catch (Exception)
        {
            ErrorMessage =
                "We could not load your publications right now. " +
                "Please try again or contact the faculty-profile administrator.";
        }
    }

    private void ValidateYearRange()
    {
        if (FromYear.HasValue && ToYear.HasValue && FromYear > ToYear)
        {
            throw new InvalidOperationException(
                "From year cannot be greater than to year.");
        }

        foreach (var year in new[] { FromYear, ToYear })
        {
            if (year.HasValue &&
                (year < 1800 || year > DateTime.UtcNow.Year + 1))
            {
                throw new InvalidOperationException(
                    "One of the publication years is outside the allowed range.");
            }
        }
    }
}
