namespace OpenAlex.Data.Models;

public sealed class DashboardSnapshot
{
    public long Institutions { get; set; }
    public long Authors { get; set; }
    public long Works { get; set; }
    public long Faculty { get; set; }
    public long AuthorCandidates { get; set; }
    public long AuthorDecisions { get; set; }
    public long PublicationCandidates { get; set; }
    public long PublicationDecisions { get; set; }
    public long AcceptedPublications { get; set; }
    public long OperationalErrors { get; set; }
    public DateTime? LastIngestionRunAtUtc { get; set; }
    public DateTime? LastOperationalLogAtUtc { get; set; }
}
