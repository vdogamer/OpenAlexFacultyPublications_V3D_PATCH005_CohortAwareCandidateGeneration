namespace OpenAlex.Data.Models;

public sealed class FacultyListItem
{
    public string FacultyKey { get; set; } = string.Empty;

    public string? UvaUserId { get; set; }

    public long FacultyId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? PrimaryEmail { get; set; }

    public string? DepartmentName { get; set; }

    public string? DivisionName { get; set; }

    public string? AppointmentTitle { get; set; }

    public string? Orcid { get; set; }

    public string? ClaimedOpenAlexAuthorId { get; set; }

    public bool IsActive { get; set; } = true;

    public bool DepartmentDataComplete { get; set; }

    public int PublicationCount { get; set; }
}