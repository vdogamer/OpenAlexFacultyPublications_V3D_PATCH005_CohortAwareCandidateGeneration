namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCohortEvidenceEnhancementOptions
{
    public string CohortName { get; set; } = "V3E_PATCH017B8_FIVE_FACULTY";
    public long SourceCandidateIngestionRunId { get; set; } = 20083;
    public int ExpectedCandidateCount { get; set; } = 161;
    public bool ConfirmEnhancement { get; set; }
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B11A";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B11A-EvidenceEnhancement";
}
