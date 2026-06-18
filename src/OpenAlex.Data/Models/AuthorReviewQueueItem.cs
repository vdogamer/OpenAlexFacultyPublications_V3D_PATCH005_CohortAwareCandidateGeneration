namespace OpenAlex.Data.Models;

public sealed class AuthorReviewQueueItem
{
    public long FacultyAuthorMatchCandidateId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? FacultyEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOpenAlexId { get; set; }
    public string? AuthorOrcid { get; set; }
    public decimal? MatchScore { get; set; }
    public string? CandidateStatus { get; set; }
    public string? EvidenceSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
