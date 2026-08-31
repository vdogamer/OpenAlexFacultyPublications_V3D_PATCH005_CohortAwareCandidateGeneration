namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCohortExecutionOptions
{
    public string CohortName { get; set; } = "V3E_PATCH017B8_FIVE_FACULTY";
    public int ExpectedFacultyCount { get; set; } = 5;
    public int ExpectedAcceptedAuthorDecisionCount { get; set; } = 13;
    public int ExpectedRawSourceRowCount { get; set; } = 193;
    public int ExpectedDeduplicatedCandidateCount { get; set; } = 161;
    public int ExpectedDuplicateRawRowsRemoved { get; set; } = 32;
    public int ExpectedExistingCandidateCount { get; set; } = 0;
    public int MaxCandidates { get; set; } = 200;
    public bool ConfirmExecution { get; set; }
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B11";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B11-5FacultyPubExec";
}
