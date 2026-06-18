namespace OpenAlex.Data.Models;

public sealed class InstitutionUpsertResult
{
    public long InstitutionId { get; set; }
    public string OpenAlexId { get; set; } = string.Empty;
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
}
