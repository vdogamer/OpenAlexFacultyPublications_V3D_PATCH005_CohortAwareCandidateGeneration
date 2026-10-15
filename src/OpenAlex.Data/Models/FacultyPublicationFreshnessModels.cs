namespace OpenAlex.Data.Models;

public sealed class FacultyPublicationFreshnessIdentity
{
    public string FacultyKey { get; init; } = string.Empty;
    public long DepartmentId { get; init; }
    public string? UvaUserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string? DivisionName { get; init; }
    public string? Position { get; init; }
    public string PublicationIdentitySource { get; init; } = string.Empty;
    public string SourceAuthorId { get; init; } = string.Empty;
    public string? AcademicOnlineAuthorId { get; init; }
    public string? OpenAlexAuthorId { get; init; }
    public string? OpenAlexResolutionStatus { get; init; }
    public long? DataRunId { get; init; }
    public bool DepartmentDataComplete { get; init; }
}

public sealed class FacultyPublicationFreshnessReviewState
{
    public string? Doi { get; init; }
    public string Title { get; init; } = string.Empty;
    public int? PublicationYear { get; init; }
    public string ReviewStatus { get; init; } = string.Empty;
    public string? DecisionStatus { get; init; }
    public bool IsCurrentSource { get; init; }
}

public sealed class FacultyPublicationDoiReviewAssociation
{
    public string UvaUserId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int? PublicationYear { get; init; }
    public string ReviewStatus { get; init; } = string.Empty;
    public string? DecisionStatus { get; init; }
    public bool IsCurrentSource { get; init; }
}

public sealed class FacultyPublicationManualReviewSubmissionResult
{
    public long FacultyPublicationReviewCandidateId { get; init; }
    public long PublicationKey { get; init; }
    public bool WasCreated { get; init; }
    public string SubmissionStatus { get; init; } = string.Empty;
}
