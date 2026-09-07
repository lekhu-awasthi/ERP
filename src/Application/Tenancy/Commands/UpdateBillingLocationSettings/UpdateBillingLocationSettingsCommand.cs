using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Queries.GetBillingLocationSettings;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateBillingLocationSettings;

/// <summary>
/// Phase 32 -- writes the Advanced panel on Organization &gt; Features &gt; Billing Location.
///
/// <para>Its existence is the point, not an afterthought: phase-31's lesson (a) is that a tenant
/// field with no command behind it is an absent feature, and
/// <see cref="TenantSettings.LocationWiseReportPermission"/> would otherwise have shipped in exactly
/// that state -- schema'd, seeded, and unreachable. It has no enforcer until phase 32b, which is
/// recorded on the field itself and in the status doc; it does have a command, an endpoint and a
/// screen today.</para>
/// </summary>
public sealed record UpdateBillingLocationSettingsCommand(
    Guid OrganizationId,
    LocationScopeMode LocationScopeMode,
    bool LocationWiseReportPermission)
    : IRequest<BillingLocationSettingsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BillingLocationManage;
}
