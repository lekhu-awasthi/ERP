using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateRolePermissions;

/// <summary>
/// A bulk replace over potentially 100+ RolePermission rows per save -- <see cref="Grants"/> is
/// the *complete* desired grant state for every PermissionKeyCatalog key (the matrix page submits
/// its whole checkbox grid each save, not just the keys that changed), and the handler diffs that
/// against each row's existing IsGranted rather than blindly clearing and re-adding every row (see
/// CLAUDE.md's own known-gotchas entry on the Phase 4 Clear+re-Add InMemory-provider mis-tracking
/// bug -- the same "don't rely on ORM fixup for a full-collection replace" discipline applies here
/// even though RolePermission isn't a child collection of an aggregate).
/// </summary>
public sealed record UpdateRolePermissionsCommand(Guid OrganizationId, Guid RoleId, IReadOnlyDictionary<string, bool> Grants)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleManage;
}
