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
    private readonly FacultyProfileHandoffService _profileHandoff;

    public MyPublicationsModel(
        FacultyAcademicOnlineMappingRepository mappingRepository,
        AcademicOnlineFacultyPublicationsService publicationsService,
        AcademicOnlinePublicationHtmlFormatter formatter,
        FacultyProfileHandoffService profileHandoff)
    {
        _mappingRepository = mappingRepository;
        _publicationsService = publicationsService;
        _formatter = formatter;
        _profileHandoff = profileHandoff;
    }

    [BindProperty(SupportsGet = true)]
    public bool VerifiedOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Range { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ToYear { get; set; }

    public AcademicOnlineFacultyPublicationsResult? Result { get; private set; }
    public string? GeneratedHtmlFragment { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? SignedInDisplayName { get; private set; }
    public string? FacultyProfileEditUrl { get; private set; }
    public bool DirectPublishingEnabled => _profileHandoff.DirectPublishingEnabled;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            ApplyRangePreset();
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

            FacultyProfileEditUrl = _profileHandoff.BuildPublicationsEditorUrl(
                computingId,
                mapping.FacultyEmail ?? email,
                userName);
        }
        catch (Exception)
        {
            ErrorMessage =
                "We could not load your publications right now. " +
                "Please try again or contact the faculty-profile administrator.";
        }
    }

    public string RangeLabel =>
        NormalizeRange() switch
        {
            "5" => "Last 5 years",
            "10" => "Last 10 years",
            "custom" => BuildCustomRangeLabel(),
            _ => "All publications"
        };

    private void ApplyRangePreset()
    {
        var currentYear = DateTime.UtcNow.Year;

        switch (NormalizeRange())
        {
            case "5":
                FromYear = currentYear - 4;
                ToYear = currentYear;
                Range = "5";
                break;

            case "10":
                FromYear = currentYear - 9;
                ToYear = currentYear;
                Range = "10";
                break;

            case "custom":
                Range = "custom";
                break;

            default:
                Range = "all";
                FromYear = null;
                ToYear = null;
                break;
        }
    }

    private string NormalizeRange()
    {
        return string.IsNullOrWhiteSpace(Range)
            ? "all"
            : Range.Trim().ToLowerInvariant();
    }

    private string BuildCustomRangeLabel()
    {
        if (FromYear.HasValue && ToYear.HasValue)
        {
            return $"{FromYear.Value}–{ToYear.Value}";
        }

        if (FromYear.HasValue)
        {
            return $"{FromYear.Value} and later";
        }

        if (ToYear.HasValue)
        {
            return $"Through {ToYear.Value}";
        }

        return "Custom range";
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
