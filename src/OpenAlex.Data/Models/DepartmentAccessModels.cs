namespace OpenAlex.Data.Models;

public sealed class DepartmentAccessItem
{
    public long DepartmentId { get; set; }

    public string DepartmentName { get; set; }
        = string.Empty;

    public bool CanReview { get; set; }
}