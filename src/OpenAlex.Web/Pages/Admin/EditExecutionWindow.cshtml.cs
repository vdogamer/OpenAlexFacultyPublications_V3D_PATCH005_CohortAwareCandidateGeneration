using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;
namespace OpenAlex.Web.Pages.Admin;
[Authorize(Policy = "AdminPolicy")]
public sealed class EditExecutionWindowModel : PageModel
{
    private readonly ExecutionWindowRepository _repository;
    public EditExecutionWindowModel(ExecutionWindowRepository repository) => _repository = repository;
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty] public string WindowName { get; set; } = string.Empty;
    [BindProperty] public string? WindowDescription { get; set; }
    [BindProperty] public string? AppliesToJobName { get; set; }
    [BindProperty] public bool IsEnabled { get; set; } = true;
    [BindProperty] public string DaysOfWeekCsv { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday";
    [BindProperty] public string StartTimeLocalText { get; set; } = "19:00";
    [BindProperty] public string EndTimeLocalText { get; set; } = "22:00";
    [BindProperty] public string TimeZoneId { get; set; } = "Eastern Standard Time";
    [BindProperty] public bool AllowFullLoad { get; set; }
    [BindProperty] public int MaxRequestsPerWindow { get; set; } = 1;
    public string? ErrorMessage { get; private set; }
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id <= 0) return;
        var window = await _repository.GetWindowAsync(Id, cancellationToken); if (window is null) return;
        WindowName=window.WindowName; WindowDescription=window.WindowDescription; AppliesToJobName=window.AppliesToJobName; IsEnabled=window.IsEnabled; DaysOfWeekCsv=window.DaysOfWeekCsv; StartTimeLocalText=window.StartTimeLocal.ToString(@"hh\:mm"); EndTimeLocalText=window.EndTimeLocal.ToString(@"hh\:mm"); TimeZoneId=window.TimeZoneId; AllowFullLoad=window.AllowFullLoad; MaxRequestsPerWindow=window.MaxRequestsPerWindow;
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TimeSpan.TryParse(StartTimeLocalText, out var start)) { ErrorMessage="Start time must be valid, such as 19:00."; return Page(); }
        if (!TimeSpan.TryParse(EndTimeLocalText, out var end)) { ErrorMessage="End time must be valid, such as 22:00."; return Page(); }
        await _repository.SaveWindowAsync(new JobExecutionWindow { JobExecutionWindowId=Id, WindowName=WindowName, WindowDescription=WindowDescription, AppliesToJobName=AppliesToJobName, IsEnabled=IsEnabled, DaysOfWeekCsv=DaysOfWeekCsv, StartTimeLocal=start, EndTimeLocal=end, TimeZoneId=TimeZoneId, AllowFullLoad=AllowFullLoad, MaxRequestsPerWindow=MaxRequestsPerWindow }, GetAdminName(), cancellationToken);
        return RedirectToPage("/Admin/ExecutionWindows");
    }
    private string GetAdminName() => User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name) ? User.Identity.Name! : "local.demo.admin";
}
