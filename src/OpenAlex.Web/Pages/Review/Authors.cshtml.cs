using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class AuthorsModel : PageModel
{
    private readonly ReviewQueueRepository _repository;
    private readonly ReviewDecisionRepository _decisionRepository;

    public AuthorsModel(
        ReviewQueueRepository repository,
        ReviewDecisionRepository decisionRepository)
    {
        _repository = repository;
        _decisionRepository = decisionRepository;
    }

    public IReadOnlyList<AuthorReviewQueueItem> Items { get; private set; } = Array.Empty<AuthorReviewQueueItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _repository.GetAuthorQueueAsync(200, cancellationToken);
    }

    public async Task<IActionResult> OnPostAcceptAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.AcceptAuthorCandidateAsync(id, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(long id, string? notes, CancellationToken cancellationToken)
    {
        await _decisionRepository.RejectAuthorCandidateAsync(id, GetReviewerName(), notes, cancellationToken);
        return RedirectToPage();
    }

    private string GetReviewerName()
    {
        return User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name)
            ? User.Identity.Name!
            : "local.demo.reviewer";
    }
}
