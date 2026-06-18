namespace OpenAlex.Matching;

public sealed class FacultyAuthorMatchInput
{
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? FacultyOrcid { get; set; }
    public string? ClaimedOpenAlexAuthorId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;
    public string? AuthorOrcid { get; set; }
    public string? AuthorOpenAlexId { get; set; }
    public string? DepartmentName { get; set; }
    public string? LastKnownInstitutionName { get; set; }
}
