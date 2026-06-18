using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages;

public sealed class FacultyModel : PageModel
{
    private readonly FacultyRepository _facultyRepository;

    public FacultyModel(FacultyRepository facultyRepository)
    {
        _facultyRepository = facultyRepository;
    }

    public string? Search { get; private set; }
    public IReadOnlyList<FacultyListItem> Faculty { get; private set; } = Array.Empty<FacultyListItem>();

    public async Task OnGetAsync(string? search, CancellationToken cancellationToken)
    {
        Search = search;
        Faculty = await _facultyRepository.GetFacultyAsync(search, 200, cancellationToken);
    }
}
