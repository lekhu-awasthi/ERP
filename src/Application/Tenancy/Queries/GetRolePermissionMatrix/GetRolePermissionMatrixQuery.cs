using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetRolePermissionMatrix;

/// <summary>
/// Every PermissionKeys constant (via PermissionKeyCatalog), grouped by module, left-joined
/// against RoleId's existing RolePermission rows (a key with no row defaults to IsGranted=false).
/// Works for a system role too (IsSystemRole=true) so an Admin can view Admin/Member's own grants
/// as reference -- only UpdateRolePermissionsCommand actually blocks mutating a system role.
///
/// <para><b>Phase 32b returns a second section.</b> The live editor is two top-level collapsibles --
/// <i>Organization-wide Permissions</i> ("Apply across all billing locations") and
/// <i>Location-specific Permissions</i> ("Scoped to individual billing locations"), one sub-section
/// per location holding the Transactions group alone. <see cref="RolePermissionMatrixDto.Groups"/>
/// is the first, unchanged; <see cref="RolePermissionMatrixDto.LocationSections"/> is the second, and
/// is empty for a tenant with a single location (Decision D -- nothing to scope).</para>
/// </summary>
public sealed record GetRolePermissionMatrixQuery(Guid OrganizationId, Guid RoleId)
    : IRequest<RolePermissionMatrixDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleView;
}

public sealed record PermissionMatrixEntryDto(string PermissionKey, bool IsGranted);

public sealed record PermissionMatrixGroupDto(string Module, IReadOnlyList<PermissionMatrixEntryDto> Permissions);

/// <summary>
/// One billing location's slice of the Location-specific section. <see cref="Groups"/> holds only
/// <c>LocationScopedPermissions.ScopableKeys</c> -- the transaction keys -- because General, Settings
/// and Reports are organization-wide, confirmed live on 2026-09-07 and again on 2026-09-09 with
/// <c>LocationWiseReportPermission</c> switched on.
/// </summary>
public sealed record LocationPermissionSectionDto(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    IReadOnlyList<PermissionMatrixGroupDto> Groups);

public sealed record RolePermissionMatrixDto(
    Guid RoleId,
    string RoleName,
    bool IsSystemRole,
    IReadOnlyList<PermissionMatrixGroupDto> Groups,
    IReadOnlyList<LocationPermissionSectionDto> LocationSections);
