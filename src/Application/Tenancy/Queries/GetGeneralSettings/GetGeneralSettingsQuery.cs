using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetGeneralSettings;

/// <summary>
/// Phase 31 -- reads the five behaviour switches on <c>TenantSettings</c> that
/// <see cref="Commands.UpdateGeneralSettings.UpdateGeneralSettingsCommand"/> writes. Takes that
/// command's key rather than a View key of its own, exactly as
/// <c>GetAccountingDefaultsQuery</c> takes <c>AccountingDefaultsManage</c>; see
/// <see cref="PermissionKeys.GeneralSettingsManage"/> for why there is only one key.
///
/// <para><b>Why this query did not exist before.</b> Four of these five fields were schema'd in
/// phase 2 and have been reachable from nothing ever since -- no command, no endpoint, no screen --
/// which is why the roadmap called three of them "dead settings". <c>NegativeStockBalanceAction</c>
/// was the exception in that it was <i>read</i> (by <c>FifoStockAvailabilityPolicy</c>), but even it
/// could never be moved off its seeded default. This query and its command are the enabler the rest
/// of phase 31 stands on: phase-29's lesson, that a tenant-level field with no screen behind it is
/// not shipped.</para>
/// </summary>
public sealed record GetGeneralSettingsQuery(Guid OrganizationId)
    : IRequest<GeneralSettingsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.GeneralSettingsManage;
}

public sealed record GeneralSettingsDto(
    SuggestSellingPriceMode SuggestSellingPriceMode,
    ProductPriceBasis ProductPriceBasis,
    InventoryTrackingMode InventoryTrackingMode,
    BalanceAction NegativeCashBalanceAction,
    BalanceAction NegativeStockBalanceAction,
    BalanceAction CreditLimitExceedsAction);
