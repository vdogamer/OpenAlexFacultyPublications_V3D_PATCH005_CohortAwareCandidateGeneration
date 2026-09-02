namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineScholarSearchCandidate
{
    public string AuthorId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Position { get; init; }
    public string? Department { get; init; }
    public string? Institution { get; init; }
    public string? OrcidUrl { get; init; }
    public decimal? RankingScore { get; init; }
}
