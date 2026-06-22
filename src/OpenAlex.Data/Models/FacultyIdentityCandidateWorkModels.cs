namespace OpenAlex.Data.Models;

public sealed class FacultyIdentityCandidateForWorkExpansion
{
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string? CandidateDisplayName { get; set; }
    public string? CandidateOrcid { get; set; }
    public int? WorksCount { get; set; }
    public decimal BestEvidenceScore { get; set; }
    public string BestEvidenceStatus { get; set; } = string.Empty;
    public string BestCandidateType { get; set; } = string.Empty;
}

public sealed class FacultyIdentityCandidateWorkWriteModel
{
    public long FacultyId { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string WorkOpenAlexId { get; set; } = string.Empty;
    public string? WorkOpenAlexShortId { get; set; }
    public string? WorkDisplayName { get; set; }
    public string? WorkDoi { get; set; }
    public int? PublicationYear { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string? WorkType { get; set; }
    public string? MatchedAuthorDisplayName { get; set; }
    public string? MatchedAuthorPosition { get; set; }
    public string? MatchedAuthorInstitutionsJson { get; set; }
    public string? AuthorshipsJson { get; set; }
    public string? PrimaryLocationJson { get; set; }
    public string? LocationsJson { get; set; }
    public string? WorkRawJson { get; set; }
    public string? EvidenceSummary { get; set; }
    public long IngestionRunId { get; set; }
}

public sealed class FacultyIdentityCandidateWorkUpsertResult
{
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
}
