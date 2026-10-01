using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.SetLocationPosMode;

/// <summary>
/// Phase 60 -- puts a till at a location, changes which one, or takes it away (phase 59
/// Decision B). Its own command rather than a field on UpdateBillingLocationCommand: that command
/// replaces a location whole, so a client that did not know about the mode would switch every till
/// off by saving an address; and the mode is configured where it acts, on the POS screen.
///
/// <para><b>Not</b> behind <see cref="IRequireAnyFeature"/>, deliberately. Which entitlement is
/// needed depends on the mode asked for -- Retail needs POS Retail, Restaurant needs POS Restaurant
/// -- and <see cref="Domain.Tenancy.PosMode.None"/> needs neither, so that a tenant which loses an
/// entitlement can still switch its tills off (phase-20f: a flag-off tenant must still function).
/// The handler checks, as phase 32's location cap does.</para>
/// </summary>
public sealed record SetLocationPosModeCommand(Guid OrganizationId, Guid LocationId, PosMode PosMode)
    : IRequest<PosLocationSettingsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;
}
