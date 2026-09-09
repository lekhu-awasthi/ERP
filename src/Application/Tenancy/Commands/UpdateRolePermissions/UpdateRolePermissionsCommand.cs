using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateRolePermissions;

/// <summary>
/// One location's slice of a save (phase 32b). <see cref="Grants"/> is that location's complete
/// desired state over <c>LocationScopedPermissions.ScopableKeys</c> -- the transaction keys and
/// nothing else, matching what the live editor renders per location.
/// </summary>
public sealed record LocationGrantsInput(Guid LocationId, IReadOnlyDictionary<string, bool> Grants);

/// <summary>
/// A bulk replace over potentially 100+ RolePermission rows per save -- <see cref="Grants"/> is
/// the *complete* desired grant state for every PermissionKeyCatalog key (the matrix page submits
/// its whole checkbox grid each save, not just the keys that changed), and the handler diffs that
/// against each row's existing IsGranted rather than blindly clearing and re-adding every row (see
/// CLAUDE.md's own known-gotchas entry on the Phase 4 Clear+re-Add InMemory-provider mis-tracking
/// bug -- the same "don't rely on ORM fixup for a full-collection replace" discipline applies here
/// even though RolePermission isn't a child collection of an aggregate).
///
/// <para><b><see cref="LocationGrants"/> is phase 32b's second half</b>, and it is optional and
/// trailing: a client that never sends it -- every pre-32b caller, and every tenant with one
/// location -- leaves location-scoped rows exactly as they were rather than having them silently
/// revoked. A location that <i>is</i> present is a complete replace for that location, same
/// contract as <see cref="Grants"/>.</para>
/// </summary>
public sealed record UpdateRolePermissionsCommand(
    Guid OrganizationId,
    Guid RoleId,
    IReadOnlyDictionary<string, bool> Grants,
    IReadOnlyList<LocationGrantsInput>? LocationGrants = null)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleManage;
}
