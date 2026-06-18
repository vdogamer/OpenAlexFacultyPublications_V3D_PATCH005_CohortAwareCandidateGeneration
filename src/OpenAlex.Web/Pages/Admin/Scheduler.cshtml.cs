using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class SchedulerModel : PageModel
{
    private readonly SchedulerRepository _schedulerRepository;
    public SchedulerModel(SchedulerRepository schedulerRepository) => _schedulerRepository = schedulerRepository;
    public SchedulerDashboardSnapshot Snapshot { get; private set; } = new();
    public async Task OnGetAsync(CancellationToken cancellationToken) => Snapshot = await _schedulerRepository.GetDashboardAsync(cancellationToken);
}
