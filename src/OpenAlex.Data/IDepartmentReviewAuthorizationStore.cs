using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public interface IDepartmentReviewAuthorizationStore
{
    Task<AppUserRoleProfile?> FindUserAsync(string userName, CancellationToken cancellationToken = default);
    Task<bool> UserCanReviewDepartmentAsync(long appUserId, long departmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepartmentAccessItem>> GetReviewDepartmentsForUserAsync(long appUserId, CancellationToken cancellationToken = default);
}
