using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Diagnostics;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class PreflightModel : PageModel
{
    private readonly ConfigurationPreflightService _preflightService;
    public PreflightModel(ConfigurationPreflightService preflightService) => _preflightService = preflightService;
    public ConfigurationValidationResult Result { get; private set; } = new();
    public void OnGet() => Result = _preflightService.Validate();
}
