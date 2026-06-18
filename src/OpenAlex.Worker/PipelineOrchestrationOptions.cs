namespace OpenAlex.Worker;

public sealed class PipelineOrchestrationOptions
{
    public bool StopOnFirstFailure { get; set; } = true;

    public string[] JobSequence { get; set; } =
    [
        "InstitutionIngestion",
        "AuthorIngestion",
        "WorkIngestion",
        "FacultyAuthorCandidateGeneration",
        "FacultyPublicationCandidateGeneration"
    ];
}
