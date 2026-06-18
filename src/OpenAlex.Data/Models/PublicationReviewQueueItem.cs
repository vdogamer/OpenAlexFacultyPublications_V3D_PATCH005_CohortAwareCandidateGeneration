namespace OpenAlex.Data.Models;

public sealed class PublicationReviewQueueItem
{
    public long FacultyPublicationCandidateId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string WorkTitle { get; set; } = string.Empty;
    public int? PublicationYear { get; set; }
    public string? WorkDoi { get; set; }
    public string? DoiUrl { get; set; }
    public string? WorkOpenAlexId { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? CandidateStatus { get; set; }
    public decimal? MatchScore { get; set; }
    public string? EvidenceSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
