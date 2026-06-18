using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Operations;

[Authorize(Policy = "AdminPolicy")]
public sealed class LogsModel : PageModel
{
    private readonly OperationalDashboardRepository _repository;

    public LogsModel(OperationalDashboardRepository repository)
    {
        _repository = repository;
    }

    public GenericOperationalTable Table { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Table = await _repository.GetOperationalLogsAsync(300, cancellationToken);
    }
}
