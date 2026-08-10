namespace ErpApp.Application.Common.Security;

/// <summary>
/// Declares the permission key (architecture-spec.md §3.7, see <see cref="PermissionKeys"/>) a
/// command/query requires. Checked by AuthorizationBehavior in the MediatR pipeline
/// (Common/Behaviors/AuthorizationBehavior.cs) -- a request that doesn't implement this
/// interface skips the authorization check entirely (e.g. queries with no gate yet).
/// </summary>
public interface IRequirePermission
{
    string PermissionKey { get; }
}
