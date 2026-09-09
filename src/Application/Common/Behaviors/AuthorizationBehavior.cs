using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Checks a request's declared permission key (<see cref="IRequirePermission"/>) against the
/// current user's role-granted permissions in the relevant Organization (architecture-spec.md
/// §3.7), replacing the ad hoc "is this user an Accepted Admin member" checks Phase 1b inlined
/// in InviteUserCommandHandler/AcceptRequestCommandHandler. Requests that don't implement
/// IRequirePermission skip this behavior entirely (return await next() immediately) -- most
/// queries and Phase 0/1a/1b commands have no permission gate yet.
///
/// <para><b>Phase 32b changes exactly two things here, and both are load-bearing.</b> First, the
/// grant join now requires <c>LocationId == null</c>: a location-scoped row grants its key at one
/// branch, and letting it satisfy this organization-wide check would silently promote a
/// single-branch grant to a tenant-wide one. Second, a caller who fails that check gets a
/// location-scoped second chance in <see cref="EnsureGrantedByLocationAsync"/> when the key is one
/// of the 77 that can carry a location. That is phase-27a's <c>AttachmentAccess</c> pattern for the
/// third time, promoted from a per-handler re-check into the pipeline because this instance spans
/// ~110 requests rather than one, and a single missed re-check would be an open door rather than a
/// bug.</para>
///
/// <para><b>Why it is not a sixth behavior of its own.</b> The location check needs to know whether
/// the organization-wide check already passed, and passing that between two behaviors means a scoped
/// context object which a nested <c>ISender.Send</c> would overwrite -- a correctness bug that only
/// shows up on the handful of handlers that send other requests. Keeping both halves in one method
/// also makes it impossible to add a request that clears the first gate and silently skips the
/// second, which is the failure mode this whole phase is guarding against.</para>
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(IAppDbContext db, ICurrentUserService currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IRequirePermission permissionRequest)
        {
            return await next();
        }

        var organizationId = request is IOrganizationScoped scoped ? scoped.OrganizationId : (Guid?)null;

        if (organizationId is null && request is ITargetsMembership membershipRequest)
        {
            var targetMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.Id == membershipRequest.MembershipId, cancellationToken)
                ?? throw new NotFoundException("Membership not found.");

            organizationId = targetMembership.OrganizationId;
        }

        // No OrganizationId to check against (e.g. CreateOrganizationCommand) -- a global
        // permission, granted to any authenticated user. See IOrganizationScoped's remarks.
        if (organizationId is not null)
        {
            var isGranted = await (
                from membership in db.OrganizationMemberships
                where membership.OrganizationId == organizationId
                      && membership.UserId == currentUser.UserId
                      && membership.Status == MembershipStatus.Accepted
                join rolePermission in db.RolePermissions
                    on membership.RoleId equals rolePermission.RoleId
                where rolePermission.PermissionKey == permissionRequest.PermissionKey
                      && rolePermission.IsGranted
                      && rolePermission.LocationId == null
                select rolePermission.Id
            ).AnyAsync(cancellationToken);

            if (!isGranted)
            {
                await EnsureGrantedByLocationAsync(
                    request, organizationId.Value, permissionRequest.PermissionKey, cancellationToken);
            }
        }

        return await next();
    }

    private static ForbiddenException Forbidden(string permissionKey) =>
        new($"You do not have permission to perform this action ({permissionKey}).");

    /// <summary>
    /// The location-scoped second chance (phase 32b). Only ever reached when the organization-wide
    /// check has already failed, which is what keeps Decision D honest: a caller holding the key
    /// organization-wide -- every Admin, and every role on a tenant that never opens the
    /// Location-specific section -- short-circuits above and pays not one extra query.
    ///
    /// <para>Throws the identical <see cref="ForbiddenException"/> shape either way, so a caller
    /// cannot tell which of the two checks refused them; the only difference is that a
    /// location-scoped refusal <i>names the location</i>
    /// (<c>"HeadOffice.Sales.Invoice.Approve"</c>), because a user who is refused needs to know
    /// which chip to ask for.</para>
    /// </summary>
    private async Task EnsureGrantedByLocationAsync(
        object request, Guid organizationId, string permissionKey, CancellationToken cancellationToken)
    {
        if (!LocationScopedPermissions.IsLocationScopable(permissionKey)
            || LocationScopedPermissions.DocumentTypeOf(permissionKey) is not { } documentType)
        {
            throw Forbidden(permissionKey);
        }

        var grantedLocations = await GrantedPermissionReader.GrantedLocationsAsync(
            db, organizationId, currentUser.UserId, permissionKey, cancellationToken);

        if (grantedLocations.Count == 0)
        {
            throw Forbidden(permissionKey);
        }

        // A list narrows its own rows to the caller's locations, and a location-agnostic helper has
        // no row to narrow. Both are satisfied by holding the key at any one location -- refusing
        // them would leave a branch-scoped Member able to create an invoice but unable to open the
        // invoice list or preview its posting. See ILocationFilteredQuery / ILocationAgnosticRequest.
        if (request is ILocationFilteredQuery or ILocationAgnosticRequest)
        {
            return;
        }

        var (outcome, candidates) = await LocationScopeResolver.CandidateLocationsAsync(
            db, organizationId, documentType, request, cancellationToken);

        // The row does not exist. Let it through so the handler answers 404 -- that is the leg of
        // phase-31's both-directions proof which shows the caller does hold the pipeline key, and a
        // nonexistent id discloses nothing. A row that *does* exist outside their locations still
        // gets the 403 below, which is the direction that matters.
        if (outcome == LocationScopeOutcome.TargetMissing)
        {
            return;
        }

        // No location dimension at all: the type is out of scope for this tenant, or the row predates
        // the scope being widened and carries no location. Nothing a location grant can cover, so the
        // organization-wide refusal stands.
        if (outcome == LocationScopeOutcome.NoLocation)
        {
            throw Forbidden(permissionKey);
        }

        foreach (var candidate in candidates)
        {
            if (grantedLocations.Contains(candidate))
            {
                continue;
            }

            var code = await db.BillingLocations
                .Where(x => x.Id == candidate)
                .Select(x => x.Code)
                .SingleOrDefaultAsync(cancellationToken) ?? candidate.ToString();

            throw Forbidden(LocationScopedPermissions.Describe(code, permissionKey));
        }
    }
}
