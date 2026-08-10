namespace ErpApp.Application.Common.Security;

/// <summary>
/// Implemented by an <see cref="IRequirePermission"/> request that carries its target
/// OrganizationId directly (e.g. InviteUserCommand), so AuthorizationBehavior can check the
/// current user's role-granted permissions there without an extra lookup. A request that
/// implements <see cref="IRequirePermission"/> but neither this nor
/// <see cref="ITargetsMembership"/> (e.g. CreateOrganizationCommand, which by definition
/// predates any membership in the Organization it's creating) is treated as a global
/// permission: any authenticated user may proceed.
/// </summary>
public interface IOrganizationScoped
{
    Guid OrganizationId { get; }
}
