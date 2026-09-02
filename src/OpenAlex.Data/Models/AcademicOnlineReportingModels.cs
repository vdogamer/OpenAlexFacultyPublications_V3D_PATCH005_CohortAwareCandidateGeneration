namespace OpenAlex.Data.Models;

public sealed class AcademicOnlineDepartmentListItem
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int ScholarCount { get; init; }
    public int PublicationCount { get; init; }
    public DateTime LastRetrievedAtUtc { get; init; }
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
