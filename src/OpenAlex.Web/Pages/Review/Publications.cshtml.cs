using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class PublicationsModel : PageModel
{
    private readonly ReviewQueueRepository _repository;
    private readonly ReviewDecisionRepository _decisionRepository;

    public PublicationsModel(
        ReviewQueueRepository repository,
        ReviewDecisionRepository decisionRepository)
    {
        _repository = repository;
        _decisionRepository = decisionRepository;
    }

    public IReadOnlyList<PublicationReviewQueueItem> Items { get; private set; } = Array.Empty<PublicationReviewQueueItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _repository.GetPublicationQueueAsync(200, cancellationToken);
    }

    public async Task<IActionResult> OnPostAcceptAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.AcceptPublicationCandidateAsync(id, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.RejectPublicationCandidateAsync(id, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostKeepSeparateAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.KeepNearDuplicatePublicationCandidateSeparateAsync(id, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSuppressNearDuplicateWarningAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.SuppressNearDuplicatePublicationCandidateWarningAsync(id, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkDuplicateOfCandidateAsync(long id, long relatedCandidateId, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.MarkPublicationCandidateAsNearDuplicateAsync(id, relatedCandidateId, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    private string GetReviewerName()
    {
        return User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name)
            ? User.Identity.Name!
            : "local.demo.reviewer";
    }
}
