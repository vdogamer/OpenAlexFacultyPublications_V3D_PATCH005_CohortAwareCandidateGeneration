using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly DashboardRepository _dashboardRepository;

    public IndexModel(DashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public DashboardSnapshot Snapshot { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _dashboardRepository.GetSnapshotAsync(cancellationToken);
    }
}
