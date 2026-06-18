using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class JobRequestsModel : PageModel
{
    private readonly SchedulerRepository _schedulerRepository;
    public JobRequestsModel(SchedulerRepository schedulerRepository) => _schedulerRepository = schedulerRepository;
    public IReadOnlyList<JobExecutionRequestListItem> Requests { get; private set; } = Array.Empty<JobExecutionRequestListItem>();
    public async Task OnGetAsync(CancellationToken cancellationToken) => Requests = await _schedulerRepository.GetRecentRequestsAsync(200, cancellationToken);
}
