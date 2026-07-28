namespace OpenAlex.Data.Models;

public sealed class FacultyAuthorIdentityResolutionPreviewSourceRow
{
    public long SourceWorkBackfillRunId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? FacultyFirstName { get; set; }
    public string? FacultyLastName { get; set; }
    public string? FacultyOrcid { get; set; }
    public string? FacultyClaimedOpenAlexAuthorId { get; set; }
    public string? DepartmentName { get; set; }
    public string? AcademicOnlineName { get; set; }
    public string? AcademicOnlineEmail { get; set; }
    public string? AcademicOnlineOrcidUrl { get; set; }
    public int SourceWorkCount { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorDisplayName { get; set; }
    public string? CandidateAuthorOrcid { get; set; }
    public int WorkAppearanceCount { get; set; }
    public int CorrespondingAuthorWorkCount { get; set; }
    public int AuthorshipInstitutionCount { get; set; }
    public int UvaAffiliationWorkCount { get; set; }
    public string? AppearingWorkOpenAlexIds { get; set; }
    public long? ExistingIdentitySummaryId { get; set; }
    public string? ExistingIdentityTriageStatus { get; set; }
    public decimal? ExistingIdentityEvidenceScore { get; set; }
    public string? CurrentIdentityDecisionStatus { get; set; }
    public int ExistingIdentityCandidateDiscoveryCount { get; set; }
    public int CurrentAcceptedAuthorDecisionCount { get; set; }

    public bool ExactFacultyOrcidMatch { get; set; }
    public bool ExactAcademicOnlineOrcidMatch { get; set; }
    public bool ExactClaimedOpenAlexIdMatch { get; set; }
    public int NameEvidenceScore { get; set; }
    public bool AppearsOnAllSelectedWorks { get; set; }
    public decimal ProposedIdentityScore { get; set; }
    public int CandidateRank { get; set; }
    public string PreviewStatus { get; set; } = string.Empty;
    public string ProposedNextAction { get; set; } = string.Empty;
    public string EvidenceSummary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = string.Empty;
}

public sealed class FacultyAuthorIdentityResolutionSafetyCounts
{
    public int IdentityCandidateRows { get; set; }
    public int IdentityDecisionRows { get; set; }
    public int AuthorDecisionRows { get; set; }
}
