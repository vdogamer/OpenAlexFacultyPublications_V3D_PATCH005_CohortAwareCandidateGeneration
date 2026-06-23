using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Review;

[Authorize(Policy = "ReviewerPolicy")]
public sealed class IdentitiesModel : PageModel
{
    private readonly IdentityReviewRepository _repository;

    public IdentitiesModel(IdentityReviewRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<IdentityReviewListItem> Items { get; private set; } = Array.Empty<IdentityReviewListItem>();

    public string? Search { get; private set; }
    public string? TriageStatus { get; private set; }
    public string? DecisionStatus { get; private set; }

    public async Task OnGetAsync(
        string? search,
        string? triageStatus,
        string? decisionStatus,
        CancellationToken cancellationToken)
    {
        Search = search;
        TriageStatus = triageStatus;
        DecisionStatus = decisionStatus;
        Items = await _repository.GetIdentityReviewListAsync(search, triageStatus, decisionStatus, 250, cancellationToken);
    }
}
