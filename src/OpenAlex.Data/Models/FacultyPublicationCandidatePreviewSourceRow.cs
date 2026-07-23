namespace OpenAlex.Data.Models;

public sealed class FacultyPublicationCandidatePreviewSourceRow
{
    public long SourceBackfillItemId { get; set; }
    public long SourceWorkBackfillRunId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public long WorkId { get; set; }
    public string WorkOpenAlexId { get; set; } = string.Empty;
    public string? WorkOpenAlexShortId { get; set; }
    public string? WorkTitle { get; set; }
    public string? WorkDoi { get; set; }
    public int? PublicationYear { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string? WorkType { get; set; }
    public int AcceptedDecisionCount { get; set; }
    public long? FacultyAuthorMatchDecisionId { get; set; }
    public long? AuthorId { get; set; }
    public string? AuthorOpenAlexId { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOrcid { get; set; }
    public long? WorkAuthorshipId { get; set; }
    public int? OrdinalPosition { get; set; }
    public bool? IsCorresponding { get; set; }
    public int AuthorshipInstitutionCount { get; set; }
    public int MatchingInstitutionCount { get; set; }
    public long? ExistingCandidateId { get; set; }
    public decimal ProposedMatchScore { get; set; }
    public string PreviewStatus { get; set; } = string.Empty;
    public string EvidenceSummary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = string.Empty;
}
