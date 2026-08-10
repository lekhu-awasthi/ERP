namespace ErpApp.Application.Common.Security;

/// <summary>
/// Implemented by an <see cref="IRequirePermission"/> request that targets an existing
/// OrganizationMembership by id (e.g. AcceptRequestCommand) rather than carrying an
/// OrganizationId directly -- AuthorizationBehavior resolves the relevant Organization from
/// that membership row before checking the current user's role-granted permissions there.
/// </summary>
public interface ITargetsMembership
{
    Guid MembershipId { get; }
}
