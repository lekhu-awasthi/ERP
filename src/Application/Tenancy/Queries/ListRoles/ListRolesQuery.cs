using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListRoles;

/// <summary>
/// The two shared system rows (OrganizationId null) unioned with this Organization's own custom
/// rows -- powers both the Role Reference list page and the invite flow's role picker (Phase 14).
/// Gated on RoleView (Admin-only, see PermissionKeys.RoleView's doc comment); a custom role that's
/// been granted OrganizationInviteUser without also being granted RoleView would 403 loading this
/// list and thus the invite dropdown -- a known, accepted interaction rather than something this
/// phase solves, the same kind of phase-scoped edge this codebase has tolerated before (e.g. Phase
/// 12's flagged-but-not-fixed Phase 1c Role-UI gap).
/// </summary>
public sealed record ListRolesQuery(Guid OrganizationId) : IRequest<IReadOnlyList<RoleDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleView;
}

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystemRole);
