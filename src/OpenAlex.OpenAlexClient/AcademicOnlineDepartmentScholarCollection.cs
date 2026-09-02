namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineDepartmentScholarCollection
{
    public IReadOnlyList<AcademicOnlineScholarSearchCandidate> Scholars { get; init; } =
        Array.Empty<AcademicOnlineScholarSearchCandidate>();

    public int PagesFetched { get; init; }
    public int RowsPerPage { get; init; }
    public bool HitMaxPages { get; init; }
}
