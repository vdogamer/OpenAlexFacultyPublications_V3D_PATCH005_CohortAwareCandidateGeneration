using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Faculty;

public sealed class PublicationsModel : PageModel
{
    private readonly FacultyPublicationRepository
        _repository;

    public PublicationsModel(
        FacultyPublicationRepository repository)
    {
        _repository = repository;
    }

    public FacultyPublicationProfile? Faculty
    {
        get;
        private set;
    }

    public IReadOnlyList<FacultyPublicationListItem>
        Publications
    {
        get;
        private set;
    } =
        Array.Empty<FacultyPublicationListItem>();


    public async Task<IActionResult> OnGetAsync(
        string? facultyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                facultyKey))
        {
            return BadRequest();
        }

        Faculty =
            await _repository.GetFacultyAsync(
                facultyKey,
                cancellationToken);

        if (Faculty is null)
        {
            return NotFound();
        }

        Publications =
            await _repository.GetPublicationsAsync(
                facultyKey,
                top: 500,
                cancellationToken);

        return Page();
    }
}