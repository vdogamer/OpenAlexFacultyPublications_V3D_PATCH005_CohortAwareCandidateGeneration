using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Operations;

[Authorize(Policy = "AdminPolicy")]
public sealed class RequestsModel : PageModel
{
    private readonly OperationalDashboardRepository _repository;

    public RequestsModel(OperationalDashboardRepository repository)
    {
        _repository = repository;
    }

    public GenericOperationalTable Table { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Table = await _repository.GetRequestLogsAsync(200, cancellationToken);
    }
}
