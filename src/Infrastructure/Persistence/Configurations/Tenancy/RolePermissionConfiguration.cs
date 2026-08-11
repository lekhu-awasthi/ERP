using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

/// <summary>
/// Seed grants for Phase 1c's two system roles against every PermissionKey currently checked by
/// AuthorizationBehavior (PermissionKeys.OrganizationInviteUser/OrganizationAcceptRequest) --
/// Admin=granted, Member=explicitly denied (an explicit IsGranted=false row rather than the row
/// simply being absent, matching architecture-spec.md §3.7's RolePermission shape and making the
/// "Member can't do this" intent visible in the seed data rather than implicit). Every later
/// phase's PermissionKeys constants need their own Admin/Member seed rows added here (or, once
/// the roadmap's later Role Reference editor exists, real per-tenant data instead of a seed).
/// </summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    private static readonly Guid AdminInviteUserId = Guid.Parse("00000000-0000-0000-0002-000000000001");
    private static readonly Guid AdminAcceptRequestId = Guid.Parse("00000000-0000-0000-0002-000000000002");
    private static readonly Guid MemberInviteUserId = Guid.Parse("00000000-0000-0000-0002-000000000003");
    private static readonly Guid MemberAcceptRequestId = Guid.Parse("00000000-0000-0000-0002-000000000004");

    // Phase 2 (Configuration foundation) -- Admin granted on View+Manage for every lookup type;
    // Member granted View only, Manage explicitly denied (an IsGranted=false row, matching the
    // existing denial-row convention above). Judgment call: Configuration lookups are tenant-wide
    // control-plane settings, not per-user working data, so Member-read/Admin-write fits the
    // existing Admin/Member split's intent -- see phase-2-status.md's scope decisions.
    private static readonly Guid AdminCreditTermViewId = Guid.Parse("00000000-0000-0000-0002-000000000005");
    private static readonly Guid AdminCreditTermManageId = Guid.Parse("00000000-0000-0000-0002-000000000006");
    private static readonly Guid MemberCreditTermViewId = Guid.Parse("00000000-0000-0000-0002-000000000007");
    private static readonly Guid MemberCreditTermManageId = Guid.Parse("00000000-0000-0000-0002-000000000008");

    private static readonly Guid AdminPaymentModeViewId = Guid.Parse("00000000-0000-0000-0002-000000000009");
    private static readonly Guid AdminPaymentModeManageId = Guid.Parse("00000000-0000-0000-0002-00000000000a");
    private static readonly Guid MemberPaymentModeViewId = Guid.Parse("00000000-0000-0000-0002-00000000000b");
    private static readonly Guid MemberPaymentModeManageId = Guid.Parse("00000000-0000-0000-0002-00000000000c");

    private static readonly Guid AdminCustomStatusViewId = Guid.Parse("00000000-0000-0000-0002-00000000000d");
    private static readonly Guid AdminCustomStatusManageId = Guid.Parse("00000000-0000-0000-0002-00000000000e");
    private static readonly Guid MemberCustomStatusViewId = Guid.Parse("00000000-0000-0000-0002-00000000000f");
    private static readonly Guid MemberCustomStatusManageId = Guid.Parse("00000000-0000-0000-0002-000000000010");

    private static readonly Guid AdminReportingTagCategoryViewId = Guid.Parse("00000000-0000-0000-0002-000000000011");
    private static readonly Guid AdminReportingTagCategoryManageId = Guid.Parse("00000000-0000-0000-0002-000000000012");
    private static readonly Guid MemberReportingTagCategoryViewId = Guid.Parse("00000000-0000-0000-0002-000000000013");
    private static readonly Guid MemberReportingTagCategoryManageId = Guid.Parse("00000000-0000-0000-0002-000000000014");

    private static readonly Guid AdminReportingTagOptionViewId = Guid.Parse("00000000-0000-0000-0002-000000000015");
    private static readonly Guid AdminReportingTagOptionManageId = Guid.Parse("00000000-0000-0000-0002-000000000016");
    private static readonly Guid MemberReportingTagOptionViewId = Guid.Parse("00000000-0000-0000-0002-000000000017");
    private static readonly Guid MemberReportingTagOptionManageId = Guid.Parse("00000000-0000-0000-0002-000000000018");

    private static readonly Guid AdminCustomFieldDefinitionViewId = Guid.Parse("00000000-0000-0000-0002-000000000019");
    private static readonly Guid AdminCustomFieldDefinitionManageId = Guid.Parse("00000000-0000-0000-0002-00000000001a");
    private static readonly Guid MemberCustomFieldDefinitionViewId = Guid.Parse("00000000-0000-0000-0002-00000000001b");
    private static readonly Guid MemberCustomFieldDefinitionManageId = Guid.Parse("00000000-0000-0000-0002-00000000001c");

    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", schema: "tenancy");

        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.PermissionKey).HasMaxLength(200).IsRequired();
        builder.Property(rp => rp.IsGranted).IsRequired();

        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionKey }).IsUnique();

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(
            RolePermission.Create(AdminInviteUserId, Role.AdminId, PermissionKeys.OrganizationInviteUser, true),
            RolePermission.Create(AdminAcceptRequestId, Role.AdminId, PermissionKeys.OrganizationAcceptRequest, true),
            RolePermission.Create(MemberInviteUserId, Role.MemberId, PermissionKeys.OrganizationInviteUser, false),
            RolePermission.Create(MemberAcceptRequestId, Role.MemberId, PermissionKeys.OrganizationAcceptRequest, false),

            RolePermission.Create(AdminCreditTermViewId, Role.AdminId, PermissionKeys.CreditTermView, true),
            RolePermission.Create(AdminCreditTermManageId, Role.AdminId, PermissionKeys.CreditTermManage, true),
            RolePermission.Create(MemberCreditTermViewId, Role.MemberId, PermissionKeys.CreditTermView, true),
            RolePermission.Create(MemberCreditTermManageId, Role.MemberId, PermissionKeys.CreditTermManage, false),

            RolePermission.Create(AdminPaymentModeViewId, Role.AdminId, PermissionKeys.PaymentModeView, true),
            RolePermission.Create(AdminPaymentModeManageId, Role.AdminId, PermissionKeys.PaymentModeManage, true),
            RolePermission.Create(MemberPaymentModeViewId, Role.MemberId, PermissionKeys.PaymentModeView, true),
            RolePermission.Create(MemberPaymentModeManageId, Role.MemberId, PermissionKeys.PaymentModeManage, false),

            RolePermission.Create(AdminCustomStatusViewId, Role.AdminId, PermissionKeys.CustomStatusView, true),
            RolePermission.Create(AdminCustomStatusManageId, Role.AdminId, PermissionKeys.CustomStatusManage, true),
            RolePermission.Create(MemberCustomStatusViewId, Role.MemberId, PermissionKeys.CustomStatusView, true),
            RolePermission.Create(MemberCustomStatusManageId, Role.MemberId, PermissionKeys.CustomStatusManage, false),

            RolePermission.Create(AdminReportingTagCategoryViewId, Role.AdminId, PermissionKeys.ReportingTagCategoryView, true),
            RolePermission.Create(AdminReportingTagCategoryManageId, Role.AdminId, PermissionKeys.ReportingTagCategoryManage, true),
            RolePermission.Create(MemberReportingTagCategoryViewId, Role.MemberId, PermissionKeys.ReportingTagCategoryView, true),
            RolePermission.Create(MemberReportingTagCategoryManageId, Role.MemberId, PermissionKeys.ReportingTagCategoryManage, false),

            RolePermission.Create(AdminReportingTagOptionViewId, Role.AdminId, PermissionKeys.ReportingTagOptionView, true),
            RolePermission.Create(AdminReportingTagOptionManageId, Role.AdminId, PermissionKeys.ReportingTagOptionManage, true),
            RolePermission.Create(MemberReportingTagOptionViewId, Role.MemberId, PermissionKeys.ReportingTagOptionView, true),
            RolePermission.Create(MemberReportingTagOptionManageId, Role.MemberId, PermissionKeys.ReportingTagOptionManage, false),

            RolePermission.Create(AdminCustomFieldDefinitionViewId, Role.AdminId, PermissionKeys.CustomFieldDefinitionView, true),
            RolePermission.Create(AdminCustomFieldDefinitionManageId, Role.AdminId, PermissionKeys.CustomFieldDefinitionManage, true),
            RolePermission.Create(MemberCustomFieldDefinitionViewId, Role.MemberId, PermissionKeys.CustomFieldDefinitionView, true),
            RolePermission.Create(MemberCustomFieldDefinitionManageId, Role.MemberId, PermissionKeys.CustomFieldDefinitionManage, false));
    }
}
