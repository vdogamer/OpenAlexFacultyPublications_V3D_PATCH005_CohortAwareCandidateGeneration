namespace OpenAlex.Data.Models;

public sealed class FacultyIdentityWorkForScoring
{
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? FacultyFirstName { get; set; }
    public string? FacultyLastName { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string WorkOpenAlexId { get; set; } = string.Empty;
    public string? WorkOpenAlexShortId { get; set; }
    public string? MatchedAuthorDisplayName { get; set; }
    public string? WorkDisplayName { get; set; }
    public string? WorkDoi { get; set; }
    public int? PublicationYear { get; set; }
    public string? MatchedAuthorInstitutionsJson { get; set; }
    public string? EvidenceSummary { get; set; }
}

public sealed class FacultyIdentityWorkEvidenceScoreWriteModel
{
    public long FacultyId { get; set; }
    public string CandidateAuthorOpenAlexId { get; set; } = string.Empty;
    public string? CandidateAuthorOpenAlexShortId { get; set; }
    public string WorkOpenAlexId { get; set; } = string.Empty;
    public string? WorkOpenAlexShortId { get; set; }
    public string? MatchedAuthorDisplayName { get; set; }
    public string? WorkDisplayName { get; set; }
    public int? PublicationYear { get; set; }
    public decimal NameEvidenceScore { get; set; }
    public decimal InstitutionEvidenceScore { get; set; }
    public decimal DoiEvidenceScore { get; set; }
    public decimal PublicationYearEvidenceScore { get; set; }
    public decimal OverallWorkEvidenceScore { get; set; }
    public string WorkEvidenceStatus { get; set; } = string.Empty;
    public string? EvidenceSummary { get; set; }
    public string? EvidenceJson { get; set; }
    public long IngestionRunId { get; set; }
}

public sealed class FacultyIdentityWorkEvidenceScoreUpsertResult
{
    public bool Inserted { get; set; }
    public bool Updated { get; set; }
}
