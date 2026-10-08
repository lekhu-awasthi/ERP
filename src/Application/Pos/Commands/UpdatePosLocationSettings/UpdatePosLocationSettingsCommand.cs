using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;

/// <summary>
/// Phase 60 -- saves one location's Location Settings &gt; General tab whole, as the vendor's single
/// Save does, creating the row on first save. Accounts are optional and fall back to the tenant
/// defaults at posting time (see <see cref="PosLocationSettings"/>). Allowed for a location whose
/// mode is still <see cref="PosMode.None"/>, so a till can be configured before it is switched on.
/// </summary>
public sealed record UpdatePosLocationSettingsCommand(
    Guid OrganizationId,
    Guid LocationId,
    bool ServiceChargeEnabled,
    decimal ServiceChargeRate,
    Guid? ServiceChargeAccountId,
    bool ServiceChargeOnTakeAway,
    bool RoundOffEnabled,
    Guid? RoundOffAccountId,
    bool CashVerificationRequired,
    IReadOnlyList<int> Denominations,
    PosTab? DefaultTab,
    bool PrintEstimateBill,
    bool PrintInvoice,
    bool PrintCreditNote,
    bool PrintKot,
    bool AbbreviatedTaxInvoiceEnabled)
    : IRequest<PosLocationSettingsDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
