using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 27a -- the OrganizationMemberships/RolePermissions join that
/// <see cref="Behaviors.AuthorizationBehavior{TRequest,TResponse}"/> performs, exposed for the
/// handlers that must do their own gating because the key they need is not knowable until they have
/// read a row.
///
/// <para>Phase 12's <c>TransactionApprovalQueryHandler</c> and phase 23's
/// <c>RecentTransactionsQueryHandler</c> each inlined their own copy of this join with a comment
/// saying it was copied from the other. Phase 27a would have made a third and fourth copy, so it is
/// one method now. Behaviour is deliberately identical to the behavior's own check -- Accepted
/// memberships only, <c>IsGranted</c> rows only -- because a divergence between the two would be a
/// silent authorization hole rather than a bug anyone would notice.</para>
///
/// <para><b>Phase 32b: every organization-wide read here filters <c>LocationId == null</c>.</b> That
/// is not a refinement, it is the whole correctness of the split. A <c>RolePermission</c> row with a
/// location is a grant <i>at that location only</i>; letting one fall through an unqualified
/// "does this user hold key K" question would turn a single-branch grant into an
/// organization-wide one -- the exact privilege escalation this phase exists to prevent. The
/// location-aware questions are the <c>...AtLocation</c>/<c>...Locations</c> members below, and they
/// are the only place a non-null <c>LocationId</c> is ever read.</para>
/// </summary>
public static class GrantedPermissionReader
{
    /// <summary>
    /// Every <b>organization-wide</b> permission key the current user holds in this organization --
    /// the ones the live editor labels "Apply across all billing locations". Location-scoped grants
    /// are deliberately excluded; ask <see cref="GrantedLocationsAsync"/> about those.
    /// </summary>
    public static async Task<IReadOnlySet<string>> GrantedKeysAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var keys = await (
            from membership in db.OrganizationMemberships
            where membership.OrganizationId == organizationId
                  && membership.UserId == userId
                  && membership.Status == MembershipStatus.Accepted
            join rolePermission in db.RolePermissions
                on membership.RoleId equals rolePermission.RoleId
            where rolePermission.IsGranted && rolePermission.LocationId == null
            select rolePermission.PermissionKey
        ).ToListAsync(cancellationToken);

        return keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Throws <see cref="ForbiddenException"/> unless the user holds <paramref name="permissionKey"/>
    /// -- the same exception type, and the same message shape, <c>AuthorizationBehavior</c> throws,
    /// so a caller cannot tell whether the pipeline or the handler refused them.
    /// </summary>
    public static async Task EnsureGrantedAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        var granted = await GrantedKeysAsync(db, organizationId, userId, cancellationToken);

        if (!granted.Contains(permissionKey))
        {
            throw new ForbiddenException(
                $"You do not have permission to perform this action ({permissionKey}).");
        }
    }

    /// <summary>
    /// Phase 61 -- <see cref="EnsureGrantedAsync"/> for a request acting <b>at one location</b>: held
    /// organization-wide, or (for a phase-32b scopable key) granted at <paramref name="locationId"/>.
    /// The refusal names the key the way <c>AuthorizationBehavior</c> does, including the
    /// <c>CODE.Key</c> form for a location the caller is not granted, so a handler's re-check reads
    /// exactly like the pipeline's.
    ///
    /// <para>For a key whose answer depends on the request's value or on a row the pipeline does not
    /// load -- the till's Approve-when-on-credit and open-only-where-you-sell rules -- which is the
    /// <c>AttachmentAccess</c> shape (phase 27a) with a location in it.</para>
    /// </summary>
    public static async Task EnsureGrantedAtLocationAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        string permissionKey,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var granted = await GrantedKeysAsync(db, organizationId, userId, cancellationToken);

        if (granted.Contains(permissionKey))
        {
            return;
        }

        if (!LocationScopedPermissions.IsLocationScopable(permissionKey))
        {
            throw new ForbiddenException(
                $"You do not have permission to perform this action ({permissionKey}).");
        }

        var locations = await GrantedLocationsAsync(db, organizationId, userId, permissionKey, cancellationToken);

        if (locations.Contains(locationId))
        {
            return;
        }

        var code = await db.BillingLocations
            .Where(x => x.Id == locationId && x.OrganizationId == organizationId)
            .Select(x => x.Code)
            .SingleOrDefaultAsync(cancellationToken) ?? locationId.ToString();

        throw new ForbiddenException(
            $"You do not have permission to perform this action ({LocationScopedPermissions.Describe(code, permissionKey)}).");
    }

    /// <summary>
    /// Phase 32b -- the billing locations at which this user holds <paramref name="permissionKey"/>
    /// <b>location-specifically</b>. Empty is the normal answer: the live editor's own default is
    /// 0 of 94 at every location, and a role that works purely from organization-wide grants never
    /// writes a row here.
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> GrantedLocationsAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        var locationIds = await (
            from membership in db.OrganizationMemberships
            where membership.OrganizationId == organizationId
                  && membership.UserId == userId
                  && membership.Status == MembershipStatus.Accepted
            join rolePermission in db.RolePermissions
                on membership.RoleId equals rolePermission.RoleId
            where rolePermission.IsGranted
                  && rolePermission.PermissionKey == permissionKey
                  && rolePermission.LocationId != null
            select rolePermission.LocationId!.Value
        ).ToListAsync(cancellationToken);

        return locationIds.ToHashSet();
    }

    /// <summary>
    /// Phase 32b -- every billing location at which this user holds <b>any</b> location-scoped grant.
    /// This is what "the locations they have access to" means for
    /// <c>TenantSettings.LocationWiseReportPermission</c>; see
    /// <c>Application.Common.Locations.LocationAccessScope</c>, which is the only caller and which
    /// documents why the report toggle reads transaction grants rather than report ones.
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> AnyGrantedLocationsAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var locationIds = await (
            from membership in db.OrganizationMemberships
            where membership.OrganizationId == organizationId
                  && membership.UserId == userId
                  && membership.Status == MembershipStatus.Accepted
            join rolePermission in db.RolePermissions
                on membership.RoleId equals rolePermission.RoleId
            where rolePermission.IsGranted && rolePermission.LocationId != null
            select rolePermission.LocationId!.Value
        ).ToListAsync(cancellationToken);

        return locationIds.ToHashSet();
    }
}
