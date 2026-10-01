using ErpApp.Application.Common.Security;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosLocationSettings;

/// <summary>
/// Phase 60 -- one location's point-of-sale settings and the payment modes its till offers. A
/// location never saved reads as <see cref="PosLocationSettings.CreateDefault"/>'s values with
/// <see cref="PosLocationSettingsDto.IsSaved"/> false; nothing is written by reading.
/// </summary>
public sealed record GetPosLocationSettingsQuery(Guid OrganizationId, Guid LocationId)
    : IRequest<PosLocationSettingsDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

/// <param name="DefaultTab">What was saved; null means "the mode's first tab".</param>
/// <param name="EffectiveDefaultTab">What the till will actually open on today, given the mode.
/// Differs from <paramref name="DefaultTab"/> only when a mode change left the saved tab behind.</param>
/// <param name="AvailableTabs">The tabs the location's mode has, for the Default Tab picker.</param>
public sealed record PosLocationSettingsDto(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    bool LocationIsActive,
    PosMode PosMode,
    bool IsSaved,
    bool ServiceChargeEnabled,
    decimal ServiceChargeRate,
    Guid? ServiceChargeAccountId,
    bool RoundOffEnabled,
    Guid? RoundOffAccountId,
    bool CashVerificationRequired,
    IReadOnlyList<int> Denominations,
    PosTab? DefaultTab,
    PosTab? EffectiveDefaultTab,
    IReadOnlyList<PosTab> AvailableTabs,
    bool PrintEstimateBill,
    bool PrintInvoice,
    bool PrintCreditNote,
    bool PrintKot,
    bool AbbreviatedTaxInvoiceEnabled,
    IReadOnlyList<Guid> PaymentModeIds);
