namespace OpenAlex.Data.Models;

public sealed class FacultyPublicationReviewSyncResult
{
    public long DepartmentId { get; init; }

    public long SourceRows { get; init; }

    public int UpdatedRows { get; init; }

    public int InsertedRows { get; init; }

    public int RetiredRows { get; init; }

    public long CurrentCandidateRows { get; init; }
}