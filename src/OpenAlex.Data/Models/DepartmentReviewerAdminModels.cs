namespace OpenAlex.Data.Models;

public sealed class DepartmentReviewerOption
{
    public long DepartmentId { get; set; }

    public string DepartmentName { get; set; }
        = string.Empty;

    public bool IsAssigned { get; set; }
}


public sealed class DepartmentReviewerAdminUser
{
    public long AppUserId { get; set; }

    public string UserName { get; set; }
        = string.Empty;

    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsReviewer { get; set; }

    public bool IsAdmin { get; set; }

    public string RoleSummary { get; set; }
        = string.Empty;
}

public sealed class DepartmentReviewerAccessAuditItem
{
    public long AuditId { get; set; }

    public long TargetAppUserId { get; set; }

    public string TargetUserName { get; set; }
        = string.Empty;

    public long DepartmentId { get; set; }

    public string? DepartmentName { get; set; }

    public string ChangedBy { get; set; }
        = string.Empty;

    public string ChangeType { get; set; }
        = string.Empty;

    public bool? OldCanReview { get; set; }

    public bool? NewCanReview { get; set; }

    public bool? OldIsActive { get; set; }

    public bool? NewIsActive { get; set; }

    public string? ChangeReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}