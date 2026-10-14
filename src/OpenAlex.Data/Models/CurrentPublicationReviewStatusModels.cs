namespace OpenAlex.Data.Models;

public sealed class CurrentPublicationReviewStatusSummary
{
    public long TotalCurrentCount { get; set; }
    public long PendingCount { get; set; }
    public long AcceptedCount { get; set; }
    public long RejectedCount { get; set; }
}

public sealed class CurrentPublicationReviewStatusItem
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

    public string ReviewStatus { get; set; } = string.Empty;

    public string? DecisionStatus { get; set; }
    public string? DecisionReason { get; set; }
    public string? DecisionSource { get; set; }

    public string? ReviewerNotes { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    public string RelationshipSource { get; set; } = string.Empty;
}

public sealed class CurrentPublicationReviewDecisionHistoryItem
{
    public long FacultyPublicationReviewDecisionId { get; set; }
    public long FacultyPublicationReviewCandidateId { get; set; }

    public string? UvaUserId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;

    public long PublicationKey { get; set; }

    public string? WorkDoi { get; set; }
    public string WorkTitle { get; set; } = string.Empty;

    public string DecisionStatus { get; set; } = string.Empty;
    public string? DecisionReason { get; set; }
    public string DecisionSource { get; set; } = string.Empty;

    public string? ReviewerNotes { get; set; }

    public string ReviewedBy { get; set; } = string.Empty;
    public DateTime ReviewedAtUtc { get; set; }

    public bool IsCurrent { get; set; }
}