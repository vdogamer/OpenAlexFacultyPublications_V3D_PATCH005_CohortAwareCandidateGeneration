using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class DepartmentReviewerAccessAuditModel
    : PageModel
{
    private readonly RoleManagementRepository
        _repository;

    public DepartmentReviewerAccessAuditModel(
        RoleManagementRepository repository)
    {
        _repository = repository;
    }


    [BindProperty(SupportsGet = true)]
    public long? AppUserId { get; set; }


    public IReadOnlyList<DepartmentReviewerAdminUser>
        Users
        { get; private set; }
        = Array.Empty<DepartmentReviewerAdminUser>();


    public IReadOnlyList<DepartmentReviewerAccessAuditItem>
        AuditItems
        { get; private set; }
        = Array.Empty<DepartmentReviewerAccessAuditItem>();


    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        Users =
            await _repository
                .GetDepartmentReviewerUsersAsync(
                    cancellationToken);

        AuditItems =
            await _repository
                .GetDepartmentReviewerAccessAuditAsync(
                    AppUserId,
                    200,
                    cancellationToken);
    }
}