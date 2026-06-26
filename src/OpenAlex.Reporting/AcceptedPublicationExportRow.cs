namespace OpenAlex.Reporting;

public sealed class AcceptedPublicationExportRow
{
    public long? FacultyPublicationCandidateId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string? DivisionName { get; set; }
    public string? AppointmentTitle { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOpenAlexId { get; set; }
    public string? AuthorOrcid { get; set; }
    public string WorkTitle { get; set; } = string.Empty;
    public int? PublicationYear { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string? WorkDoi { get; set; }
    public string? DoiUrl { get; set; }
    public string? WorkOpenAlexId { get; set; }
    public string? WorkType { get; set; }
    public int? CitedByCount { get; set; }
    public string? DecisionStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerNotes { get; set; }
    public decimal? MatchScore { get; set; }
    public string? EvidenceBoost { get; set; }
    public string? AffiliatedInstitutionMatchCount { get; set; }
    public string? AffiliatedInstitutionEvidenceWeight { get; set; }
    public string? AffiliatedInstitutionNames { get; set; }
    public string? KnownCoauthorOverlapCount { get; set; }
    public string? KnownCoauthorHistoricalPublicationCount { get; set; }
    public string? KnownCoauthorNames { get; set; }
    public string? NearDuplicateResolutionStatus { get; set; }
    public long? RelatedFacultyPublicationCandidateId { get; set; }
    public string? NearDuplicateResolutionReason { get; set; }
    public string? EvidenceSummary { get; set; }
}
