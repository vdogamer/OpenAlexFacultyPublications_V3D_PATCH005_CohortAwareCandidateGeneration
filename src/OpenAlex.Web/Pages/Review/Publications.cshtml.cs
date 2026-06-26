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

    [BindProperty(SupportsGet = true)]
    public string? SearchText { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MinimumScore { get; set; }

    [BindProperty(SupportsGet = true)]
    public string InstitutionEvidenceFilter { get; set; } = "Any";

    [BindProperty(SupportsGet = true)]
    public string CoauthorEvidenceFilter { get; set; } = "Any";

    [BindProperty(SupportsGet = true)]
    public string NearDuplicateFilter { get; set; } = "Any";

    [BindProperty(SupportsGet = true)]
    public string ResolutionStatusFilter { get; set; } = "Any";

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText)
        || MinimumScore.HasValue
        || !string.Equals(InstitutionEvidenceFilter, "Any", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(CoauthorEvidenceFilter, "Any", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(NearDuplicateFilter, "Any", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(ResolutionStatusFilter, "Any", StringComparison.OrdinalIgnoreCase);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _repository.GetPublicationQueueAsync(
            200,
            SearchText,
            MinimumScore,
            InstitutionEvidenceFilter,
            CoauthorEvidenceFilter,
            NearDuplicateFilter,
            ResolutionStatusFilter,
            cancellationToken);
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
