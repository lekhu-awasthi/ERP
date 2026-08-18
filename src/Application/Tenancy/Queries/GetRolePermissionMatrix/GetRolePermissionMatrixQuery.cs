using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetRolePermissionMatrix;

/// <summary>
/// Every PermissionKeys constant (via PermissionKeyCatalog), grouped by module, left-joined
/// against RoleId's existing RolePermission rows (a key with no row defaults to IsGranted=false).
/// Works for a system role too (IsSystemRole=true) so an Admin can view Admin/Member's own grants
/// as reference -- only UpdateRolePermissionsCommand actually blocks mutating a system role.
/// </summary>
public sealed record GetRolePermissionMatrixQuery(Guid OrganizationId, Guid RoleId)
    : IRequest<RolePermissionMatrixDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleView;
}

public sealed record PermissionMatrixEntryDto(string PermissionKey, bool IsGranted);

public sealed record PermissionMatrixGroupDto(string Module, IReadOnlyList<PermissionMatrixEntryDto> Permissions);

public sealed record RolePermissionMatrixDto(
    Guid RoleId, string RoleName, bool IsSystemRole, IReadOnlyList<PermissionMatrixGroupDto> Groups);
