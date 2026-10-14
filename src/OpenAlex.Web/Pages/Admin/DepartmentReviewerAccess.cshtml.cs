using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Data.Models;

namespace OpenAlex.Web.Pages.Admin;

[Authorize(Policy = "AdminPolicy")]
public sealed class DepartmentReviewerAccessModel : PageModel
{
    private readonly RoleManagementRepository _repository;

    public DepartmentReviewerAccessModel(
        RoleManagementRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public long? AppUserId { get; set; }

    [BindProperty]
    public List<long> SelectedDepartmentIds { get; set; }
        = new();

    [BindProperty]
    public string? ChangeReason { get; set; }

    [TempData]
    public string? Message { get; set; }

    public IReadOnlyList<DepartmentReviewerAdminUser> Users
        { get; private set; }
        = Array.Empty<DepartmentReviewerAdminUser>();

    public DepartmentReviewerAdminUser? SelectedUser
        { get; private set; }

    public IReadOnlyList<DepartmentReviewerOption> Departments
        { get; private set; }
        = Array.Empty<DepartmentReviewerOption>();

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        Users =
            await _repository
                .GetDepartmentReviewerUsersAsync(
                    cancellationToken);

        if (!AppUserId.HasValue)
        {
            return Page();
        }

        SelectedUser =
            Users.FirstOrDefault(
                user =>
                    user.AppUserId ==
                    AppUserId.Value);

        if (SelectedUser is null)
        {
            return NotFound();
        }

        Departments =
            await _repository
                .GetDepartmentReviewerOptionsAsync(
                    AppUserId.Value,
                    cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        CancellationToken cancellationToken)
    {
        if (!AppUserId.HasValue)
        {
            Message =
                "Select a reviewer before saving department access.";

            return RedirectToPage();
        }

        var users =
            await _repository
                .GetDepartmentReviewerUsersAsync(
                    cancellationToken);

        var selectedUser =
            users.FirstOrDefault(
                user =>
                    user.AppUserId ==
                    AppUserId.Value);

        if (selectedUser is null)
        {
            return NotFound();
        }

        if (selectedUser.IsAdmin)
        {
            Message =
                "Admin users already have global department access. "
                + "Department assignments are only enforced for scoped reviewers.";

            return RedirectToPage(
                new
                {
                    AppUserId
                });
        }

        await _repository
            .SaveDepartmentReviewerAccessAsync(
                AppUserId.Value,
                SelectedDepartmentIds,
                GetAdminName(),
                ChangeReason,
                cancellationToken);

        Message =
            $"Department review access saved for {selectedUser.UserName}.";

        return RedirectToPage(
            new
            {
                AppUserId
            });
    }

    private string GetAdminName()
    {
        return User?.Identity?.IsAuthenticated == true
               && !string.IsNullOrWhiteSpace(
                   User.Identity.Name)

            ? User.Identity.Name!

            : "local.demo.admin";
    }
}
