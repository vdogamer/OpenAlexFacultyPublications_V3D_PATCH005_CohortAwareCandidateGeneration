namespace OpenAlex.Data.Models;

public sealed class FacultyIdentityCandidateSummarySource
{
    public long FacultyId { get; set; }
    public string? FacultyDisplayName { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string? CandidateDisplayName { get; set; }
    public string? CandidateOrcid { get; set; }
    public int? WorksCount { get; set; }
    public int? CitedByCount { get; set; }
    public int DiscoveryMethodCount { get; set; }
    public string? DiscoveryMethods { get; set; }
    public decimal BestIdentityEvidenceScore { get; set; }
    public string? BestIdentityEvidenceStatus { get; set; }
    public string? BestCandidateType { get; set; }
    public int ExpandedWorkRows { get; set; }
    public int DistinctExpandedWorks { get; set; }
    public int ScoredWorkRows { get; set; }
    public int StrongWorkEvidenceRows { get; set; }
    public int ModerateWorkEvidenceRows { get; set; }
    public int WeakWorkEvidenceRows { get; set; }
    public int ConflictingWorkEvidenceRows { get; set; }
    public decimal? AverageWorkEvidenceScore { get; set; }
    public decimal? BestWorkEvidenceScore { get; set; }
    public int? EarliestPublicationYear { get; set; }
    public int? LatestPublicationYear { get; set; }
}

public sealed class FacultyIdentityCandidateSummaryWriteModel : FacultyIdentityCandidateSummarySource
{
    public string IdentityReviewTriageStatus { get; set; } = string.Empty;
    public int ReviewPriority { get; set; }
    public string? SummaryText { get; set; }
    public string? EvidenceJson { get; set; }
    public long IngestionRunId { get; set; }
}

public sealed class FacultyIdentityCandidateSummaryUpsertResult
{
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
}
