using ErpApp.Application.Common.Exceptions;
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
                where rolePermission.PermissionKey == permissionRequest.PermissionKey && rolePermission.IsGranted
                select rolePermission.Id
            ).AnyAsync(cancellationToken);

            if (!isGranted)
            {
                throw new ForbiddenException(
                    $"You do not have permission to perform this action ({permissionRequest.PermissionKey}).");
            }
        }

        return await next();
    }
}
