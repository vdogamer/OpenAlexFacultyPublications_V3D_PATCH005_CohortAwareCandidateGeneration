namespace OpenAlex.Data.Models;

public sealed class FacultySearchPage
{
    public IReadOnlyList<FacultyListItem> Items { get; init; } =
        Array.Empty<FacultyListItem>();

    public int TotalCount { get; init; }
}