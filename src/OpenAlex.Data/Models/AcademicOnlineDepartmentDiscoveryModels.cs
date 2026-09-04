namespace OpenAlex.Data.Models;

public sealed class AcademicOnlineDepartmentDiscoveryClaim
{
    public long RequestId { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public int RowsPerPage { get; init; }
    public int MaxPages { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
    public string LeaseOwner { get; init; } = string.Empty;
    public DateTime LeaseUntilUtc { get; init; }
}

public sealed class AcademicOnlineDiscoveredDepartmentRecord
{
    public string? AcademicOnlineDepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? ParentAcademicOnlineDepartmentId { get; init; }
    public string? ParentName { get; init; }
    public string? ParentSlug { get; init; }
    public int ScholarCount { get; init; }
}

public sealed class AcademicOnlineDepartmentDiscoveryStatus
{
    public long RequestId { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public int RowsPerPage { get; init; }
    public int MaxPages { get; init; }
    public string RequestStatus { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTime? LeaseUntilUtc { get; init; }
    public long? DiscoveryRunId { get; init; }
    public string? ErrorText { get; init; }

    public string? RunStatus { get; init; }
    public int? PagesFetched { get; init; }
    public int? ScholarsScanned { get; init; }
    public int? AffiliationsObserved { get; init; }
    public int? DepartmentsDiscovered { get; init; }
    public int? DepartmentsInserted { get; init; }
    public int? DepartmentsUpdated { get; init; }
    public bool? HitMaxPages { get; init; }
    public string? SummaryText { get; init; }
}


public sealed class AcademicOnlineDepartmentDiscoverySeed
{
    public long SeedId { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public string OfficialName { get; init; } = string.Empty;
    public string AcademicOnlineSearchName { get; init; } = string.Empty;
    public string DepartmentCategory { get; init; } = string.Empty;
    public int SearchPriority { get; init; }
}
