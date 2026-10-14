namespace OpenAlex.Data.Models;

public sealed class CurrentPublicationReviewQueueItem
{
    public long FacultyPublicationReviewCandidateId { get; set; }

    public string? UvaUserId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string? PrimaryEmail { get; set; }

    public string DepartmentName { get; set; } = string.Empty;
    public string? DivisionName { get; set; }

    public string WorkTitle { get; set; } = string.Empty;
    public string? Venue { get; set; }
    public int? PublicationYear { get; set; }

    public string? WorkDoi { get; set; }
    public string? DoiUrl { get; set; }

    public string ReviewStatus { get; set; } = "Pending";

    public string RelationshipSource { get; set; } = string.Empty;

    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}