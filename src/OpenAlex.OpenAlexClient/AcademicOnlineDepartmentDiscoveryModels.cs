namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineDepartmentDiscoveryItem
{
    public string? AcademicOnlineDepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? ParentAcademicOnlineDepartmentId { get; init; }
    public string? ParentName { get; init; }
    public string? ParentSlug { get; init; }
    public int ScholarCount { get; init; }
}

public sealed class AcademicOnlineDepartmentDiscoveryCollection
{
    public IReadOnlyList<AcademicOnlineDepartmentDiscoveryItem> Departments { get; init; } =
        Array.Empty<AcademicOnlineDepartmentDiscoveryItem>();

    public int PagesFetched { get; init; }
    public int RowsPerPage { get; init; }
    public int ScholarsScanned { get; init; }
    public int AffiliationsObserved { get; init; }
    public int ProfilesFetched { get; init; }
    public int ProfileValidationFailures { get; init; }
    public int DepartmentsUnresolved { get; init; }
    public bool HitMaxPages { get; init; }
}


public sealed class AcademicOnlineDepartmentSeedCandidate
{
    public string OfficialName { get; init; } = string.Empty;
    public string SearchName { get; init; } = string.Empty;
    public int SearchPriority { get; init; }
    public string? ValidationParentName { get; init; }
}
