namespace OpenAlex.Data.Models;

public sealed class PublicationProvenanceDataRow
{
    public long FacultyPublicationCandidateId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public long? WorkId { get; set; }
    public string? WorkOpenAlexId { get; set; }
    public string WorkTitle { get; set; } = string.Empty;
    public string? WorkDoi { get; set; }
    public int? PublicationYear { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOpenAlexId { get; set; }
    public decimal? MatchScore { get; set; }
    public string? PublicationCandidateSource { get; set; }
    public string? PublicationCandidateStatus { get; set; }
    public string? IdentityCandidateSource { get; set; }
    public string? IdentityDecisionSource { get; set; }
    public string? IdentityDecisionStatus { get; set; }
    public string? IdentityReviewedBy { get; set; }
    public DateTime? IdentityReviewedAtUtc { get; set; }
    public string? PublicationDecisionSource { get; set; }
    public string? PublicationDecisionStatus { get; set; }
    public string? PublicationReviewedBy { get; set; }
    public DateTime? PublicationReviewedAtUtc { get; set; }
    public string MetadataSource { get; set; } = "OpenAlex";
    public bool AcademicOnlineObserved { get; set; }
    public bool? AcademicOnlineVerified { get; set; }
    public bool? AcademicOnlineVerifiedQuery { get; set; }
    public long? AcademicOnlineComparisonRunId { get; set; }
    public string? AcademicOnlineAuthorId { get; set; }
    public string? AcademicOnlineScholarName { get; set; }
    public string? AcademicOnlineMatchMethod { get; set; }
    public string ResponsibilitySummary { get; set; } = string.Empty;
}
