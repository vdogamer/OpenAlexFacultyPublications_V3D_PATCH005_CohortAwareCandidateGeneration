namespace OpenAlex.Data.Models;

public sealed class IdentityReviewListItem
{
    public long FacultyIdentityCandidateSummaryId { get; set; }
    public long FacultyId { get; set; }
    public string? FacultyDisplayName { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string? CandidateDisplayName { get; set; }
    public string? CandidateOrcid { get; set; }
    public string? IdentityReviewTriageStatus { get; set; }
    public int ReviewPriority { get; set; }
    public string? BestCandidateType { get; set; }
    public decimal BestIdentityEvidenceScore { get; set; }
    public decimal? BestWorkEvidenceScore { get; set; }
    public int DiscoveryMethodCount { get; set; }
    public string? DiscoveryMethods { get; set; }
    public int DistinctExpandedWorks { get; set; }
    public int StrongWorkEvidenceRows { get; set; }
    public int ModerateWorkEvidenceRows { get; set; }
    public int ConflictingWorkEvidenceRows { get; set; }
    public string? CurrentDecisionStatus { get; set; }
    public string? CurrentReviewerName { get; set; }
    public DateTime? CurrentDecisionCreatedAtUtc { get; set; }
}

public sealed class IdentityReviewDetail
{
    public IdentityReviewListItem Summary { get; set; } = new();
    public string? SummaryText { get; set; }
    public string? EvidenceJson { get; set; }
    public int? WorksCount { get; set; }
    public int? CitedByCount { get; set; }
    public int ScoredWorkRows { get; set; }
    public int WeakWorkEvidenceRows { get; set; }
    public decimal? AverageWorkEvidenceScore { get; set; }
    public int? EarliestPublicationYear { get; set; }
    public int? LatestPublicationYear { get; set; }
    public string? CurrentDecisionStatus { get; set; }
    public string? CurrentReviewNotes { get; set; }
    public string? CurrentReviewerName { get; set; }
    public DateTime? CurrentDecisionCreatedAtUtc { get; set; }
    public IReadOnlyList<IdentityReviewWorkEvidenceItem> WorkEvidence { get; set; } = Array.Empty<IdentityReviewWorkEvidenceItem>();
}

public sealed class IdentityReviewWorkEvidenceItem
{
    public string? WorkOpenAlexShortId { get; set; }
    public string? WorkOpenAlexId { get; set; }
    public string? WorkDisplayName { get; set; }
    public int? PublicationYear { get; set; }
    public string? MatchedAuthorDisplayName { get; set; }
    public string? WorkEvidenceStatus { get; set; }
    public decimal OverallWorkEvidenceScore { get; set; }
    public decimal NameEvidenceScore { get; set; }
    public decimal InstitutionEvidenceScore { get; set; }
    public decimal DoiEvidenceScore { get; set; }
    public decimal PublicationYearEvidenceScore { get; set; }
    public string? EvidenceSummary { get; set; }
}
