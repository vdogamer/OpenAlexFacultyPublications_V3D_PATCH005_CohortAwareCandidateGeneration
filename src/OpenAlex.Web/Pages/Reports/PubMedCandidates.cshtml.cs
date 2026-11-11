using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Web.Services.PubMed;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class PubMedCandidatesModel : PageModel
{
    private readonly PubMedCandidateLookupService _service;
    private readonly ILogger<PubMedCandidatesModel> _logger;

    public PubMedCandidatesModel(PubMedCandidateLookupService service, ILogger<PubMedCandidatesModel> logger)
    { _service = service; _logger = logger; }

    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Search { get; set; }
    [BindProperty(SupportsGet = true)] public PubMedSearchMode Mode { get; set; }
    [BindProperty(SupportsGet = true)] public string? AuthorTerms { get; set; }
    [BindProperty(SupportsGet = true)] public string? Orcid { get; set; }
    [BindProperty(SupportsGet = true)] public string? Identifiers { get; set; }
    [BindProperty(SupportsGet = true)] public string? Affiliation { get; set; }
    [BindProperty(SupportsGet = true)] public int? YearFrom { get; set; }
    [BindProperty(SupportsGet = true)] public int? YearTo { get; set; }
    [BindProperty(SupportsGet = true)] public int Limit { get; set; } = 25;

    public PubMedFacultyContext? Faculty { get; private set; }
    public bool FacultyNotFound { get; private set; }
    public PubMedQuery? Query { get; private set; }
    public PubMedCandidateResult? Result { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        { ErrorMessage = "Check the search options and use valid publication years and result limits."; return; }
        if (UserId is null)
        { if (Search) ErrorMessage = "Enter a faculty ComputingID first."; return; }
        try
        {
            Faculty = await _service.ResolveFacultyAsync(UserId, cancellationToken);
            if (Faculty is null) { FacultyNotFound = true; return; }
            UserId = Faculty.UserId;
            if (string.IsNullOrWhiteSpace(AuthorTerms) && !Search)
                AuthorTerms = PubMedQueryBuilder.SuggestAuthorTerms(Faculty.Names);
            if (!Search) return;
            Query = PubMedQueryBuilder.Build(new PubMedSearchInput(Mode, AuthorTerms, Orcid,
                Identifiers, Affiliation, YearFrom, YearTo, Limit));
            Result = await _service.SearchAsync(Faculty, Query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (PubMedInputException ex) { ErrorMessage = ex.Message; }
        catch (PubMedProviderException ex)
        {
            _logger.LogWarning(ex, "PubMed candidate lookup could not be completed.");
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Faculty/PubMed candidate lookup failed.");
            ErrorMessage = "The candidate lookup could not be completed. Please try again or contact the application administrator.";
        }
    }
}
