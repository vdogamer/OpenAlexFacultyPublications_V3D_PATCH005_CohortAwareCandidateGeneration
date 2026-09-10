namespace OpenAlex.Data.Models;

public sealed class FacultyListItem
{
    // New manager-facing search identity.
    public string FacultyKey { get; set; } = string.Empty;
    public string? UvaUserId { get; set; }

    // Existing presentation fields retained so we do not
    // unnecessarily break other callers.
    public long FacultyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string? DivisionName { get; set; }
    public string? AppointmentTitle { get; set; }
    public string? Orcid { get; set; }
    public string? ClaimedOpenAlexAuthorId { get; set; }
    public bool IsActive { get; set; } = true;

    // Internal display/readiness indicator.
    // We will not expose provider plumbing to faculty users.
    public bool DepartmentDataComplete { get; set; }
}