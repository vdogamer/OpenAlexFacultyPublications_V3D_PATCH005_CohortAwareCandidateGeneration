namespace OpenAlex.Data.Models;

public sealed class FacultyIdentityCandidateWriteModel
{
    public long FacultyId { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string? CandidateDisplayName { get; set; }
    public string? CandidateOrcid { get; set; }
    public int? WorksCount { get; set; }
    public int? CitedByCount { get; set; }
    public string? LastKnownInstitutionsJson { get; set; }
    public string DiscoveryMethod { get; set; } = string.Empty;
    public decimal EvidenceScore { get; set; }
    public string EvidenceStatus { get; set; } = "CandidateForReview";
    public string? EvidenceSummary { get; set; }
    public string? EvidenceJson { get; set; }
    public long IngestionRunId { get; set; }
}

public sealed class FacultyIdentityCandidateUpsertResult
{
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
}
