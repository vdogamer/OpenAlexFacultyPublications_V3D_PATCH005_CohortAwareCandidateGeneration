namespace OpenAlex.Data.Models;

public sealed class AcceptedIdentityBridgePreviewSourceRow
{
    public long SourceIdentityResolutionPreviewRunId { get; set; }
    public long FacultyIdentityCandidateDecisionId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public long? FacultyIdentityCandidateSummaryId { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string? CandidateDisplayName { get; set; }
    public string? CandidateOrcid { get; set; }
    public decimal? BestIdentityEvidenceScore { get; set; }
    public string? IdentitySummaryText { get; set; }
    public string? IdentityEvidenceJson { get; set; }
    public long? LocalAuthorId { get; set; }
    public long? ExistingBridgeCandidateId { get; set; }
    public string? ExistingBridgeCandidateStatus { get; set; }
    public decimal? ExistingBridgeCandidateMatchScore { get; set; }
    public long? ExistingAcceptedAuthorDecisionId { get; set; }
    public int CurrentAuthorDecisionCount { get; set; }
    public string ProposedCandidateAction { get; set; } = string.Empty;
    public string ProposedDecisionAction { get; set; } = string.Empty;
    public string PreviewStatus { get; set; } = string.Empty;
    public string ProposedNextAction { get; set; } = string.Empty;
    public string EvidenceSummary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = string.Empty;
}

public sealed class AcceptedIdentityBridgePreviewSafetyCounts
{
    public int AuthorCandidateRows { get; set; }
    public int AuthorDecisionRows { get; set; }
    public int PublicationCandidateRows { get; set; }
    public int PublicationDecisionRows { get; set; }
}
