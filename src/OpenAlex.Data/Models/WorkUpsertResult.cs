namespace OpenAlex.Data.Models;

public sealed class WorkUpsertResult
{
    public long WorkId { get; set; }
    public string OpenAlexId { get; set; } = string.Empty;
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
    public int AuthorshipRows { get; set; }
    public int AuthorshipInstitutionRows { get; set; }
    public int MatchedInstitutionRows { get; set; }
}
