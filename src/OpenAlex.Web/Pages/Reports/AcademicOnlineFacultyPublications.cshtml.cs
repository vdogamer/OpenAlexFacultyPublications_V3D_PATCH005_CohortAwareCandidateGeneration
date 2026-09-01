using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class AcademicOnlineFacultyPublicationsModel : PageModel
{
    private readonly AcademicOnlineFacultyPublicationsService _service;
    private readonly AcademicOnlinePublicationHtmlFormatter _formatter;

    public AcademicOnlineFacultyPublicationsModel(
        AcademicOnlineFacultyPublicationsService service,
        AcademicOnlinePublicationHtmlFormatter formatter)
    {
        _service = service;
        _formatter = formatter;
    }

    [BindProperty(SupportsGet = true)]
    public long FacultyId { get; set; } = 16;

    [BindProperty(SupportsGet = true)]
    public bool VerifiedOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ToYear { get; set; }

    public AcademicOnlineFacultyPublicationsResult? Result { get; private set; }
    public string? GeneratedHtmlFragment { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            ValidateYearRange();

            Result = await _service.GetAsync(
                FacultyId,
                VerifiedOnly,
                FromYear,
                ToYear,
                cancellationToken);

            GeneratedHtmlFragment = _formatter.BuildFragment(Result);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<IActionResult> OnGetHtmlAsync(CancellationToken cancellationToken)
    {
        try
        {
            ValidateYearRange();

            var result = await _service.GetAsync(
                FacultyId,
                VerifiedOnly,
                FromYear,
                ToYear,
                cancellationToken);

            return Content(
                _formatter.BuildDocument(result),
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
                "FromYear cannot be greater than ToYear.");
        }

        foreach (var year in new[] { FromYear, ToYear })
        {
            if (year.HasValue && (year < 1800 || year > DateTime.UtcNow.Year + 1))
            {
                throw new InvalidOperationException(
                    $"Publication year {year.Value} is outside the allowed range.");
            }
        }
    }
}
