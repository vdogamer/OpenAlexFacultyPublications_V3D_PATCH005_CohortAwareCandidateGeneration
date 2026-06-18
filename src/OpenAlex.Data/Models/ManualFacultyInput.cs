namespace OpenAlex.Data.Models;

public sealed class ManualFacultyInput
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string? DivisionName { get; set; }
    public string? AppointmentTitle { get; set; }
    public string? Orcid { get; set; }
    public string? ClaimedOpenAlexAuthorId { get; set; }
    public bool IsActive { get; set; } = true;
}
