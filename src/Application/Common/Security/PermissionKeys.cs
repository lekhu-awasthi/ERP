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

    // Phase 2 (Configuration foundation) -- one View/Manage pair per lookup type rather than one
    // shared key, so a later Role Reference editor can toggle e.g. "Member can edit CreditTerm"
    // independently of "Member can edit PaymentMode" (see phase-2-status.md's scope decisions).
    // .Manage covers Create/Update/Delete as a single grant -- these are simple tenant-wide named
    // lists, not warranting the Create/Edit/Delete/Approve split §3.7 reserves for transactional
    // documents.
    public const string CreditTermView = "Configuration.CreditTerm.View";
    public const string CreditTermManage = "Configuration.CreditTerm.Manage";

    public const string PaymentModeView = "Configuration.PaymentMode.View";
    public const string PaymentModeManage = "Configuration.PaymentMode.Manage";

    public const string CustomStatusView = "Configuration.CustomStatus.View";
    public const string CustomStatusManage = "Configuration.CustomStatus.Manage";

    public const string ReportingTagCategoryView = "Configuration.ReportingTagCategory.View";
    public const string ReportingTagCategoryManage = "Configuration.ReportingTagCategory.Manage";

    public const string ReportingTagOptionView = "Configuration.ReportingTagOption.View";
    public const string ReportingTagOptionManage = "Configuration.ReportingTagOption.Manage";

    public const string CustomFieldDefinitionView = "Configuration.CustomFieldDefinition.View";
    public const string CustomFieldDefinitionManage = "Configuration.CustomFieldDefinition.Manage";
}
