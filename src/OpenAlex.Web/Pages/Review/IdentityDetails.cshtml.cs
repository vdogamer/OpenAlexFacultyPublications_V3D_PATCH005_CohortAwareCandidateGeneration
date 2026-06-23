using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class IdentityDetailsModel : PageModel
{
    private readonly IdentityReviewRepository _repository;

    public IdentityDetailsModel(IdentityReviewRepository repository)
    {
        _repository = repository;
    }

    public IdentityReviewDetail? Detail { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        Detail = await _repository.GetIdentityReviewDetailAsync(id, cancellationToken);
        return Detail is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _repository.SaveDecisionAsync(id, "AcceptedIdentity", GetReviewerName(), notes, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRejectAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _repository.SaveDecisionAsync(id, "RejectedIdentity", GetReviewerName(), notes, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostNeedsMoreEvidenceAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _repository.SaveDecisionAsync(id, "NeedsMoreEvidence", GetReviewerName(), notes, cancellationToken);
        return RedirectToPage(new { id });
    }

    private string GetReviewerName()
    {
        return User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name)
            ? User.Identity.Name!
            : "local.demo.reviewer";
    }
}
