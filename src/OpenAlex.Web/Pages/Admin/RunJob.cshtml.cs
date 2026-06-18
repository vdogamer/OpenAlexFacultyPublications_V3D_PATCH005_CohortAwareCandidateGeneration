using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class RunJobModel : PageModel
{
    private readonly SchedulerRepository _schedulerRepository;
    public RunJobModel(SchedulerRepository schedulerRepository) => _schedulerRepository = schedulerRepository;
    public SchedulerJobDefinition? Job { get; private set; }
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty] public long JobDefinitionId { get; set; }
    [BindProperty] public string RunMode { get; set; } = "TestLoad";
    [BindProperty] public int? MaxPages { get; set; }
    [BindProperty] public int? MaxRecords { get; set; }
    [BindProperty] public string? RequestReason { get; set; }
    [BindProperty] public string ConfirmationText { get; set; } = string.Empty;
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Job = await _schedulerRepository.GetJobDefinitionAsync(Id, cancellationToken);
        if (Job is not null)
        {
            JobDefinitionId = Job.JobDefinitionId;
            RunMode = Job.DefaultRunMode;
            MaxPages = Job.DefaultMaxPages;
            MaxRecords = Job.DefaultMaxRecords;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Job = await _schedulerRepository.GetJobDefinitionAsync(JobDefinitionId, cancellationToken);
        if (Job is null) { ErrorMessage = "Job definition was not found."; return Page(); }
        try
        {
            await _schedulerRepository.QueueJobAsync(new QueueJobRequestModel
            {
                JobDefinitionId = JobDefinitionId,
                RequestedBy = GetRequesterName(),
                RequestReason = RequestReason,
                ConfirmationText = ConfirmationText,
                RunMode = RunMode,
                MaxPages = MaxPages,
                MaxRecords = MaxRecords
            }, cancellationToken);
            return RedirectToPage("/Admin/JobRequests");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
    private string GetRequesterName() => User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name) ? User.Identity.Name! : "local.demo.admin";
}
