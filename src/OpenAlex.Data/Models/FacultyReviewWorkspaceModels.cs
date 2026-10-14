namespace OpenAlex.Data.Models;

public sealed class FacultyReviewWorkspaceSummary
{
    public long DepartmentId { get; set; }

    public string DepartmentName { get; set; }
        = string.Empty;

    public string FacultyKey { get; set; }
        = string.Empty;

    public string? UvaUserId { get; set; }

    public string FacultyDisplayName { get; set; }
        = string.Empty;

    public string? Email { get; set; }

    public long TotalCount { get; set; }

    public long PendingCount { get; set; }

    public long AcceptedCount { get; set; }

    public long RejectedCount { get; set; }

    public long ReviewedCount =>
        AcceptedCount + RejectedCount;

    public decimal ReviewProgressPercent =>
        TotalCount == 0
            ? 0
            : Math.Round(
                ((decimal)ReviewedCount / TotalCount) * 100m,
                1);
}


public sealed class FacultyReviewWorkspaceItem
{
    public long FacultyPublicationReviewCandidateId { get; set; }

    public long PublicationKey { get; set; }

    public string? WorkDoi { get; set; }

    public string? DoiUrl { get; set; }

    public string WorkTitle { get; set; }
        = string.Empty;

    public string? Venue { get; set; }

    public int? PublicationYear { get; set; }

    public string ReviewStatus { get; set; }
        = string.Empty;

    public string? DecisionStatus { get; set; }

    public string? DecisionReason { get; set; }

    public string? ReviewerNotes { get; set; }

    public string? ReviewedBy { get; set; }

    public bool IsInCurrentReportingWindow { get; set; }

    public string ReportingWindowStatus { get; set; }
    = string.Empty;

    public DateTime? ReviewedAtUtc { get; set; }
}

public sealed class FacultyReviewWorkspacePageResult
{
    public IReadOnlyList<FacultyReviewWorkspaceItem> Items { get; set; }
        = Array.Empty<FacultyReviewWorkspaceItem>();

    public long TotalFilteredCount { get; set; }
}