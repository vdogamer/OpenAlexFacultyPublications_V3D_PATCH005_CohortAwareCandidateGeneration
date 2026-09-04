namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineDepartmentAffiliation
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? ParentId { get; init; }
    public string? ParentName { get; init; }
    public string? ParentSlug { get; init; }
    public bool IsPrimary { get; init; }
}
