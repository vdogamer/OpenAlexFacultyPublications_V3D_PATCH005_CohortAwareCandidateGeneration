namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineDepartmentSyncOptions
{
    public string DepartmentName { get; set; } = string.Empty;
    public int MaxScholarsToProcess { get; set; } = 250;
    public int ScholarRowsPerPage { get; set; } = 100;
    public int MaxScholarPages { get; set; } = 20;
    public int PublicationRowsPerPage { get; set; } = 100;
    public int MaxPublicationPages { get; set; } = 25;
    public int? FromYear { get; set; } = DateTime.UtcNow.Year - 1;
    public int? ToYear { get; set; }
    public bool VerifiedOnly { get; set; } = false;
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH019A";
    public string ApplicationVersion { get; set; } =
        "3E-PATCH019A-DepartmentCacheCompleteness";
}
