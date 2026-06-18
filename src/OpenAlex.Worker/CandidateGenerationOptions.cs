namespace OpenAlex.Worker;

public sealed class CandidateGenerationOptions
{
    public string RunMode { get; set; } = "TestLoad";
    public int MaxFacultyToProcess { get; set; } = 25;
    public int MaxAuthorsToScan { get; set; } = 500;
    public int MaxCandidatesPerFaculty { get; set; } = 10;
    public int MaxPublicationCandidatesToGenerate { get; set; } = 500;
    public string? PilotCohortName { get; set; }
    public decimal MinimumNameScore { get; set; } = 50;
    public decimal MinimumMatchScore { get; set; } = 60;
    public bool OnlyCurrentAcceptedAuthorDecisions { get; set; } = true;
    public string PublicationCandidateStatus { get; set; } = "PendingReview";
    public string StartedBy { get; set; } = "OpenAlex.V2F.Worker";
    public string ApplicationVersion { get; set; } = "2F";
}
