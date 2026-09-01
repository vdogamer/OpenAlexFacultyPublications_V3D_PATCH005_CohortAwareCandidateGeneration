using System.Text.Json;

namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlinePublicationItem
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Venue { get; init; }
    public int? PublicationYear { get; init; }
    public string? YearText { get; init; }
    public int? CitationCount { get; init; }
    public string? Doi { get; init; }
    public string? OpenAlexId { get; init; }
    public bool? Verified { get; init; }
    public string? Link { get; init; }
    public string? CoauthorsJson { get; init; }
    public string? KeywordsJson { get; init; }
    public string RawJson { get; init; } = "{}";
}

public sealed class AcademicOnlinePublicationCollection
{
    public IReadOnlyList<AcademicOnlinePublicationItem> Publications { get; init; } =
        Array.Empty<AcademicOnlinePublicationItem>();

    public int PagesFetched { get; init; }
    public int RowsPerPage { get; init; }
    public bool HitMaxPages { get; init; }
}
