using OpenAlex.Data.Models;

namespace OpenAlex.Web.Services;

public interface IFacultyPublicationFreshnessService
{
    Task<FacultyPublicationFreshnessResult> ScanByUserIdAsync(string userId, int? requestedFromYear = null, CancellationToken cancellationToken = default);
    Task<PublicationDoiLookupResult> LookupDoiAsync(string doi, CancellationToken cancellationToken = default);
    Task<FacultyPublicationManualReviewSubmissionResult> SendDoiToReviewAsync(
        string doi, string uvaUserId, long expectedDepartmentId, string submittedBy,
        long submittedByAppUserId, CancellationToken cancellationToken = default);
}
