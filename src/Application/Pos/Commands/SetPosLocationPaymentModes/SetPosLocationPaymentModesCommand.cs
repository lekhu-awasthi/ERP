using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;

/// <summary>
/// Phase 60 -- which payment modes a location's till offers: the vendor's Location Settings &gt;
/// Payment Mode tab. Replaces the whole set; an empty list unlinks everything.
///
/// <para>Every mode linked must be active and name a cash or bank account, because a till tender
/// posts to it from phase 61. Refused with a 400 naming the modes that do not, rather than linked
/// and failed later at the till, where a cashier with a customer waiting is the one who finds
/// out.</para>
/// </summary>
public sealed record SetPosLocationPaymentModesCommand(
    Guid OrganizationId,
    Guid LocationId,
    IReadOnlyList<Guid> PaymentModeIds)
    : IRequest<PosLocationSettingsDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
