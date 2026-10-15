using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class PublicationFreshnessHistoryModel : PageModel
{
    private readonly PublicationFreshnessAuditRepository _repository;

    public PublicationFreshnessHistoryModel(
        PublicationFreshnessAuditRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? ScanType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UvaUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RequestedBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AttentionOnly { get; set; }

    public IReadOnlyList<PublicationFreshnessScanAuditRow> Items { get; private set; } =
        Array.Empty<PublicationFreshnessScanAuditRow>();

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        Items =
            await _repository.GetRecentScansAsync(
                top: 200,
                scanType: ScanType,
                uvaUserId: UvaUserId,
                requestedBy: RequestedBy,
                attentionOnly: AttentionOnly,
                cancellationToken: cancellationToken);
    }
}
