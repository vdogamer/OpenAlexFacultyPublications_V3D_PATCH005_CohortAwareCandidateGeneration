using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc.RazorPages; using OpenAlex.Data; using OpenAlex.Data.Models;
namespace OpenAlex.Web.Pages.Admin;
[Authorize(Policy="AdminPolicy")]
public sealed class ExternalGroupMappingsModel:PageModel{private readonly RoleManagementRepository _repository; public ExternalGroupMappingsModel(RoleManagementRepository repository)=>_repository=repository; public IReadOnlyList<ExternalGroupRoleMapping> Mappings{get;private set;}=Array.Empty<ExternalGroupRoleMapping>(); public async Task OnGetAsync(CancellationToken ct){Mappings=await _repository.GetExternalGroupMappingsAsync(ct);}}
