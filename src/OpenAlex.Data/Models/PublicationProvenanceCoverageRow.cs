namespace OpenAlex.Data.Models;

public sealed class PublicationProvenanceCoverageRow
{
    public long AcademicOnlinePublicationSnapshotId { get; set; }
    public long AcademicOnlineComparisonRunId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? AcademicOnlineAuthorId { get; set; }
    public string? AcademicOnlineScholarName { get; set; }
    public string? AcademicOnlinePublicationId { get; set; }
    public string PublicationTitle { get; set; } = string.Empty;
    public int? PublicationYear { get; set; }
    public string? Doi { get; set; }
    public string? OpenAlexId { get; set; }
    public bool? AcademicOnlineVerified { get; set; }
    public bool AcademicOnlineVerifiedQuery { get; set; }
    public long? LocalWorkId { get; set; }
    public string? LocalWorkOpenAlexId { get; set; }
    public long? LocalCandidateId { get; set; }
    public string? LocalCandidateStatus { get; set; }
    public decimal? LocalMatchScore { get; set; }
    public string? LocalDecisionStatus { get; set; }
    public string? LocalDecisionSource { get; set; }
    public string? LocalReviewedBy { get; set; }
    public DateTime? LocalReviewedAtUtc { get; set; }
    public string MatchMethod { get; set; } = "NoLocalMatch";
    public string CoverageStatus { get; set; } = "AcademicOnlineOnly_NoLocalWork";
    public string CoverageExplanation { get; set; } = string.Empty;
}
