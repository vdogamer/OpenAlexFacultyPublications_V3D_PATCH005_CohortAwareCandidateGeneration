namespace OpenAlex.Reporting;

public sealed class AcceptedPublicationExportRow
{
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }
    public string? DepartmentName { get; set; }
    public string? DivisionName { get; set; }
    public string? AppointmentTitle { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorOpenAlexId { get; set; }
    public string? AuthorOrcid { get; set; }
    public string WorkTitle { get; set; } = string.Empty;
    public int? PublicationYear { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string? WorkDoi { get; set; }
    public string? DoiUrl { get; set; }
    public string? WorkOpenAlexId { get; set; }
    public string? WorkType { get; set; }
    public int? CitedByCount { get; set; }
    public string? DecisionStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}
