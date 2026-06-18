using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Operations;

[Authorize(Policy = "AdminPolicy")]
public sealed class HealthModel : PageModel
{
    private readonly OperationalDashboardRepository _repository;

    public HealthModel(OperationalDashboardRepository repository)
    {
        _repository = repository;
    }

    public GenericOperationalTable Table { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Table = await _repository.GetHealthChecksAsync(200, cancellationToken);
    }
}
