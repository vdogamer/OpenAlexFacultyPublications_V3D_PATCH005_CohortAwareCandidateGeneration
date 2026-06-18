using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class JobsModel : PageModel
{
    private readonly JobDashboardRepository _repository;

    public JobsModel(JobDashboardRepository repository)
    {
        _repository = repository;
    }

    public JobDashboardSnapshot Snapshot { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _repository.GetSnapshotAsync(cancellationToken);
    }
}
