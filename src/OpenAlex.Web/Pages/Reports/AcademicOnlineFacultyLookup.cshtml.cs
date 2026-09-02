using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.OpenAlexClient;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class AcademicOnlineFacultyLookupModel : PageModel
{
    private readonly AcademicOnlineUniversalFacultyLookupService _lookupService;
    private readonly AcademicOnlinePublicationHtmlFormatter _formatter;

    public AcademicOnlineFacultyLookupModel(
        AcademicOnlineUniversalFacultyLookupService lookupService,
        AcademicOnlinePublicationHtmlFormatter formatter)
    {
        _lookupService = lookupService;
        _formatter = formatter;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AuthorId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool VerifiedOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ToYear { get; set; }

    public IReadOnlyList<AcademicOnlineScholarSearchCandidate> SearchResults { get; private set; } =
        Array.Empty<AcademicOnlineScholarSearchCandidate>();

    public AcademicOnlineUniversalFacultyResult? SelectedResult { get; private set; }
    public string? GeneratedHtmlFragment { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            ValidateYearRange();

            if (!string.IsNullOrWhiteSpace(AuthorId))
            {
                SelectedResult = await _lookupService.GetPublicationsAsync(
                    AuthorId,
                    VerifiedOnly,
                    FromYear,
                    ToYear,
                    cancellationToken);

                GeneratedHtmlFragment =
                    _formatter.BuildFragment(SelectedResult.RenderResult);

                return;
            }

            if (!string.IsNullOrWhiteSpace(Search))
            {
                SearchResults = await _lookupService.SearchAsync(
                    Search,
                    Department,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<IActionResult> OnGetHtmlAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateYearRange();

            if (string.IsNullOrWhiteSpace(AuthorId))
            {
                return BadRequest("Choose a scholar first.");
            }

            var result = await _lookupService.GetPublicationsAsync(
                AuthorId,
                VerifiedOnly,
                FromYear,
                ToYear,
                cancellationToken);

            return Content(
                _formatter.BuildDocument(result.RenderResult),
                "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
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
