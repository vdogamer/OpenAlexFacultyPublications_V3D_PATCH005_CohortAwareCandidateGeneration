namespace OpenAlex.Data.Models;

public sealed class FacultyPublicationCandidateExecutionSafetyCounts
{
    public int PublicationCandidateRows { get; set; }
    public int PublicationDecisionRows { get; set; }
}

public sealed class FacultyPublicationCandidateExecutionResult
{
    public int SelectedCandidateCount { get; set; }
    public int CandidateRowsInserted { get; set; }
    public int CandidatesAlreadyExist { get; set; }
    public int ValidationFailureCount { get; set; }
}
