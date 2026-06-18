using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class AddFacultyModel : PageModel
{
    private readonly FacultyRepository _facultyRepository;

    public AddFacultyModel(FacultyRepository facultyRepository)
    {
        _facultyRepository = facultyRepository;
    }

    [BindProperty]
    public ManualFacultyInput Input { get; set; } = new();

    public IReadOnlyList<string> FacultyColumns { get; private set; } = Array.Empty<string>();

    public string? ErrorMessage { get; private set; }
    public string? SuccessMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input.IsActive = true;

        try
        {
            FacultyColumns = await _facultyRepository.GetFacultyColumnsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.DisplayName))
        {
            ErrorMessage = "Display name is required.";
            await LoadColumnsSafelyAsync(cancellationToken);
            return Page();
        }

        try
        {
            var facultyId = await _facultyRepository.AddFacultyAsync(Input, cancellationToken);
            return RedirectToPage("/Faculty", new { search = Input.DisplayName });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadColumnsSafelyAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadColumnsSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            FacultyColumns = await _facultyRepository.GetFacultyColumnsAsync(cancellationToken);
        }
        catch
        {
            FacultyColumns = Array.Empty<string>();
        }
    }
}
