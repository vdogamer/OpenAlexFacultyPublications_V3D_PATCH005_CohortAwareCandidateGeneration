namespace OpenAlex.Data.Models;

public sealed class FacultyCandidateSource
{
    public long FacultyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Orcid { get; set; }
    public string? ClaimedOpenAlexAuthorId { get; set; }
    public string? DepartmentName { get; set; }
    public string? AppointmentTitle { get; set; }
}

public sealed class PilotCohortFacultySnapshot
{
    public string CohortName { get; set; } = string.Empty;
    public int EnabledActiveFacultyCount { get; set; }
    public IReadOnlyList<long> EnabledActiveFacultyIds { get; set; } = Array.Empty<long>();
}

public sealed class AuthorCandidateSource
{
    public long AuthorId { get; set; }
    public string OpenAlexId { get; set; } = string.Empty;
    public string? OpenAlexShortId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Orcid { get; set; }
    public int? WorksCount { get; set; }
    public int? CitedByCount { get; set; }
}

public sealed class AuthorInstitutionEvidence
{
    public long AuthorId { get; set; }
    public string? InstitutionOpenAlexId { get; set; }
    public string? InstitutionDisplayName { get; set; }
    public string? InstitutionRor { get; set; }
    public string? CountryCode { get; set; }
    public string? InstitutionType { get; set; }
}

public sealed class AuthorCandidateWriteModel
{
    public long FacultyId { get; set; }
    public long AuthorId { get; set; }
    public string AuthorOpenAlexId { get; set; } = string.Empty;
    public string? AuthorOpenAlexShortId { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOrcid { get; set; }
    public string CandidateSource { get; set; } = "NameAndInstitutionSearch";
    public string CandidateStatus { get; set; } = "PendingReview";
    public decimal MatchScore { get; set; }
    public decimal NameScore { get; set; }
    public decimal OrcidScore { get; set; }
    public decimal InstitutionScore { get; set; }
    public decimal CoauthorScore { get; set; }
    public decimal PublicationScore { get; set; }
    public string? EvidenceSummary { get; set; }
    public string? EvidenceJson { get; set; }
    public long IngestionRunId { get; set; }
}

public sealed class PublicationCandidateSourceRow
{
    public long FacultyAuthorMatchDecisionId { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public long? AuthorId { get; set; }
    public string AuthorOpenAlexId { get; set; } = string.Empty;
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOrcid { get; set; }
    public long WorkId { get; set; }
    public string WorkOpenAlexId { get; set; } = string.Empty;
    public string? WorkOpenAlexShortId { get; set; }
    public string? WorkTitle { get; set; }
    public string? WorkDoi { get; set; }
    public int? PublicationYear { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string? WorkType { get; set; }
    public long WorkAuthorshipId { get; set; }
    public int? OrdinalPosition { get; set; }
    public bool? IsCorresponding { get; set; }
    public int AuthorshipInstitutionCount { get; set; }
    public int MatchingInstitutionCount { get; set; }
    public long? ExistingCandidateId { get; set; }
    public decimal MatchScore { get; set; }
    public string EvidenceSummary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = string.Empty;
}

public sealed class CandidateGenerationResult
{
    public int SourceRowsProcessed { get; set; }
    public int CandidatesInserted { get; set; }
    public int CandidatesUpdated { get; set; }
    public int CandidatesSkipped { get; set; }
}
