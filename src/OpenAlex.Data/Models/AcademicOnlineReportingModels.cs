namespace OpenAlex.Data.Models;

public sealed class AcademicOnlineDepartmentListItem
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int ScholarCount { get; init; }
    public int PublicationCount { get; init; }
    public DateTime LastRetrievedAtUtc { get; init; }
    public long? CurrentSnapshotRunId { get; init; }
    public int? ScholarsDiscovered { get; init; }
    public int? ScholarsProcessed { get; init; }
    public int? MaxScholarsToProcess { get; init; }
    public bool ScholarPaginationSafeguardHit { get; init; }
    public int PublicationPaginationSafeguardHitCount { get; init; }
    public bool? IsCacheComplete { get; init; }
    public int? SyncFromYear { get; init; }
    public int? SyncToYear { get; init; }
    public bool? SyncVerifiedOnly { get; init; }
    public DateTime? CurrentSnapshotAtUtc { get; init; }
}

public sealed class AcademicOnlineDepartmentPublicationRow
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public long PublicationKey { get; init; }
    public string? AcademicOnlinePublicationId { get; init; }
    public string? OpenAlexId { get; init; }
    public string? Doi { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Venue { get; init; }
    public string? YearText { get; init; }
    public int? PublicationYear { get; init; }
    public int? CitationCount { get; init; }
    public bool? Verified { get; init; }
    public string? Link { get; init; }
    public int FacultyCount { get; init; }
    public string FacultyNames { get; init; } = string.Empty;
    public DateTime LastRetrievedAtUtc { get; init; }
}


public sealed class AcademicOnlineDepartmentSourceUnit
{
    public long SourceUnitId { get; init; }
    public long DepartmentId { get; init; }
    public string OfficialUnitName { get; init; } = string.Empty;
    public string AcademicOnlineSearchName { get; init; } = string.Empty;
    public int SearchPriority { get; init; }
}

public sealed class AcademicOnlineDepartmentRosterMember
{
    public long RosterMemberId { get; init; }
    public long DepartmentId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? ExpectedEmail { get; init; }
    public string? AcademicOnlineAuthorId { get; init; }
    public string? OpenAlexAuthorId { get; init; }
    public int SearchPriority { get; init; }
}


public sealed class AcademicOnlineCoauthorIdentityCandidate
{
    public string AcademicOnlineAuthorId { get; init; } = string.Empty;
    public string CoauthorName { get; init; } = string.Empty;
    public int EvidencePublicationCount { get; init; }
    public int? EarliestPublicationYear { get; init; }
    public int? LatestPublicationYear { get; init; }
}
