using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages;

[Authorize(Policy = "AdminPolicy")]
public sealed class OperationsModel : PageModel
{
    private readonly OperationalDashboardRepository _repository;

    public OperationsModel(OperationalDashboardRepository repository)
    {
        _repository = repository;
    }

    public OperationalDashboardSnapshot Snapshot { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _repository.GetDashboardAsync(cancellationToken);
    }
}
