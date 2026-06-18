namespace OpenAlex.Data.Models;

public sealed class AuthorUpsertResult
{
    public long AuthorId { get; set; }
    public string OpenAlexId { get; set; } = string.Empty;
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
}
