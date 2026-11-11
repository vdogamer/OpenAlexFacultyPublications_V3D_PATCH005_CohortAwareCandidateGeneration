namespace OpenAlex.Data.Models;

public sealed class FacultyListItem
{
    public long? FacultyId { get; set; }
    public string? ComputingId { get; set; }
    public int? DirectoryPersonId { get; set; }
    public bool IsRosterMember { get; set; }
    public bool InRebuildImport { get; set; }
    public string? ImportedDisplayName { get; set; }
    public string? ImportedDepartmentName { get; set; }
    public bool HasAlternateImportedName => !string.IsNullOrWhiteSpace(ImportedDisplayName)
        && !string.Equals(DisplayName.Trim(), ImportedDisplayName.Trim(), StringComparison.OrdinalIgnoreCase);
    public string? BaselineIdentityStatus { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string? DivisionName { get; set; }
    public string? AppointmentTitle { get; set; }
    public string? Orcid { get; set; }
    public string? ClaimedOpenAlexAuthorId { get; set; }
    public bool? IsActive { get; set; }
}

public sealed record FacultyDirectoryPage(
    IReadOnlyList<FacultyListItem> Items, int TotalCount, int RosterCount,
    int AdditionalNativeCount, int PageNumber, int PageSize)
{
    public int AdditionalImportedCount { get; init; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

