using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class PublicationFreshnessModel : PageModel
{
    private readonly FacultyPublicationFreshnessService _service;

    public PublicationFreshnessModel(
        FacultyPublicationFreshnessService service)
    {
        _service = service;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Doi { get; set; }

    public FacultyPublicationFreshnessResult? Result { get; private set; }

    public PublicationDoiLookupResult? DoiResult { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? DoiErrorMessage { get; private set; }

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Doi))
        {
            try
            {
                DoiResult =
                    await _service.LookupDoiAsync(
                        Doi,
                        cancellationToken);

                Doi = DoiResult.Doi;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DoiErrorMessage = ex.Message;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(UserId))
        {
            return;
        }

        try
        {
            Result =
                await _service.ScanByUserIdAsync(
                    UserId,
                    FromYear,
                    cancellationToken);

            UserId = Result.UvaUserId;
            FromYear = Result.FromYear;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
        }
    }
}
