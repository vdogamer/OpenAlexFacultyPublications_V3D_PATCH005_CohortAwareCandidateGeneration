namespace OpenAlex.Data.Models;

public sealed class FacultyPublicationProfile
{
    public string FacultyKey { get; init; } = string.Empty;

    public long DepartmentId { get; init; }

    public string? UvaUserId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Email { get; init; }

    public string DepartmentName { get; init; } = string.Empty;

    public string? DivisionName { get; init; }

    public string? Position { get; init; }

    /*
     * Internal routing fields.
     * Do not expose these in the faculty-facing UI.
     */
    public string PublicationIdentitySource { get; init; } =
        string.Empty;

    public string SourceAuthorId { get; init; } =
        string.Empty;

    public long? DataRunId { get; init; }

    public bool DepartmentDataComplete { get; init; }
}


public sealed class FacultyPublicationListItem
{
    public long PublicationKey { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Venue { get; init; }

    public string? YearText { get; init; }

    public int? PublicationYear { get; init; }

    public string? Doi { get; init; }

    public int? CitationCount { get; init; }

    public string? Link { get; init; }
}