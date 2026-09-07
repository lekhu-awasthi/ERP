using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetBillingLocationSettings;

/// <summary>
/// Phase 32 -- reads the two controls inside Organization &gt; Features &gt; Billing Location &gt;
/// <b>Advanced</b>, plus the entitlement and the resolved document-type set the two of them imply.
///
/// <para>Takes <see cref="PermissionKeys.BillingLocationView"/>, not the Manage key its writing
/// command takes, and this deliberately breaks the GetGeneralSettings/GetAccountingDefaults
/// precedent of "the read takes the write's key". Those two screens are read by nobody but the Admin
/// editing them. <see cref="LocationScopeMode"/> is different: it decides whether a Member's own
/// Invoice form shows a location picker at all, so every Member needs to read it on every document
/// form. Gating that behind an Admin-only key would make the picker invisible to exactly the people
/// who use it.</para>
/// </summary>
public sealed record GetBillingLocationSettingsQuery(Guid OrganizationId)
    : IRequest<BillingLocationSettingsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BillingLocationView;
}

/// <param name="MultipleLocationsEnabled">The entitlement. False means the tenant is capped at its
/// seeded HeadOffice row -- the Features screen renders the card read-only and the "reach out to
/// support" note, mirroring the reference product.</param>
/// <param name="LocationBearingDocumentTypes">What <see cref="DocumentLocationScope"/> makes of
/// <paramref name="LocationScopeMode"/>, resolved server-side so the client never re-derives the
/// rule. This is what each document form asks "do I render a location picker?".</param>
public sealed record BillingLocationSettingsDto(
    LocationScopeMode LocationScopeMode,
    bool LocationWiseReportPermission,
    bool MultipleLocationsEnabled,
    IReadOnlyCollection<string> LocationBearingDocumentTypes);
