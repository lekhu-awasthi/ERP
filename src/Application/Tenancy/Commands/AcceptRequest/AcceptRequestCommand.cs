using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.AcceptRequest;

/// <summary>
/// An org admin approving someone else's pending join request (the symmetric counterpart of
/// AcceptInvitationCommand). Nothing in Phase 1b originates a Requested-status membership yet
/// -- there's no "browse/search workspaces and request to join" UI in this phase's scope, only
/// the invite-and-accept flow -- but the accept-side command is built now per the roadmap task
/// list, ready for whenever that origination flow lands.
///
/// Implements ITargetsMembership rather than IOrganizationScoped -- only MembershipId is known
/// at the API boundary (no OrganizationId in the route), so AuthorizationBehavior resolves the
/// relevant Organization from the target membership row itself.
/// </summary>
public sealed record AcceptRequestCommand(Guid MembershipId) : IRequest, IRequirePermission, ITargetsMembership
{
    public string PermissionKey => PermissionKeys.OrganizationAcceptRequest;
}
