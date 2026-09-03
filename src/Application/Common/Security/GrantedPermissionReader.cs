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
/// </summary>
public static class GrantedPermissionReader
{
    /// <summary>Every permission key the current user holds in this organization.</summary>
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
            where rolePermission.IsGranted
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
}
