namespace ErpApp.Application.Common.Security;

/// <summary>
/// Stable permission-key catalog (architecture-spec.md §3.7's "PermissionKey a stable string"),
/// checked against RolePermission rows by AuthorizationBehavior. Phase 1c seeds only the
/// handful of keys needed to prove the pipeline fires end to end (see RoleConfiguration's seed
/// data); each later phase's commands add their own constants here as they're built.
/// </summary>
public static class PermissionKeys
{
    /// <summary>Global permission: granted to any authenticated user, not checked against a
    /// specific Organization's roles (there is no Organization -- and thus no role -- yet when
    /// this fires). See IOrganizationScoped's remarks.</summary>
    public const string OrganizationCreate = "Tenancy.Organization.Create";

    public const string OrganizationInviteUser = "Tenancy.Organization.InviteUser";

    public const string OrganizationAcceptRequest = "Tenancy.Organization.AcceptRequest";
}
