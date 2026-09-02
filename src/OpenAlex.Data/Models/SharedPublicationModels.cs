namespace OpenAlex.Data.Models;

public sealed class SharedPublicationSearchResult
{
    public IReadOnlyList<ResolvedFacultyUserId> ResolvedFaculty { get; init; } = Array.Empty<ResolvedFacultyUserId>();
    public IReadOnlyList<string> MissingUserIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SharedPublicationRow> Publications { get; init; } = Array.Empty<SharedPublicationRow>();
}

public sealed class ResolvedFacultyUserId
{
    public string UserId { get; init; } = string.Empty;
    public long FacultyId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? PrimaryEmail { get; init; }
}

public sealed class SharedPublicationRow
{
    public string WorkTitle { get; init; } = string.Empty;
    public int? PublicationYear { get; init; }
    public DateTime? PublicationDate { get; init; }
    public string? WorkDoi { get; init; }
    public string? DoiUrl { get; init; }
    public string? WorkOpenAlexId { get; init; }
    public string? WorkType { get; init; }
    public int? CitedByCount { get; init; }
    public int SharedFacultyCount { get; init; }
}
