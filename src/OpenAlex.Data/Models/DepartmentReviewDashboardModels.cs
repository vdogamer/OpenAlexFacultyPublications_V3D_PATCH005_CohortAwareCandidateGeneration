namespace OpenAlex.Data.Models;

public sealed class DepartmentReviewDashboardSummary
{
    public long DepartmentId { get; set; }

    public string DepartmentName { get; set; }
        = string.Empty;

    public long TotalCount { get; set; }

    public long DistinctFacultyCount { get; set; }

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


public sealed class DepartmentFacultyReviewSummary
{
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


public sealed class ReviewDepartmentOption
{
    public long DepartmentId { get; set; }

    public string DepartmentName { get; set; }
        = string.Empty;
}