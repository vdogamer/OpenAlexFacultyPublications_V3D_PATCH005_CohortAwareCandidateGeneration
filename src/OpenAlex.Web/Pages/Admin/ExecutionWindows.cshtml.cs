using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
namespace OpenAlex.Web.Pages.Admin;
[Authorize(Policy = "AdminPolicy")]
public sealed class ExecutionWindowsModel : PageModel
{
    private readonly ExecutionWindowRepository _repository;
    public ExecutionWindowsModel(ExecutionWindowRepository repository) => _repository = repository;
    public IReadOnlyList<JobExecutionWindow> Windows { get; private set; } = Array.Empty<JobExecutionWindow>();
    public async Task OnGetAsync(CancellationToken cancellationToken) => Windows = await _repository.GetWindowsAsync(cancellationToken);
}
