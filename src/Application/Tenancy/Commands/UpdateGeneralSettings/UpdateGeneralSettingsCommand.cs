using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Queries.GetGeneralSettings;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateGeneralSettings;

/// <summary>
/// Phase 31 -- the Configurations &gt; General screen, live-confirmed 2026-09-06.
///
/// <para><b>All five switches move together, in one command.</b> The reference product auto-saves
/// each radio the instant it is clicked (there is no Save button on that page at all), which could
/// have been read as an argument for five one-field commands. It is not: the page is one form over
/// one row, and a per-field command would multiply five endpoints, five validators and five
/// authorization checks over a single aggregate that is written perhaps twice in a tenant's life.
/// The client sends the whole form. <c>InventoryTrackingMode</c> rides along even though the
/// reference product removed that control from this page (recorded in the 2026-09-02 pass) --
/// dropping it from the command would make the field permanently unwritable again, which is the
/// exact fault this phase exists to fix, and the deferred Delivery Note / GRN entry in
/// <c>roadmap.md</c> names it as the seam it will need.</para>
///
/// <para><b>Not folded into <c>UpdateAccountingDefaultsCommand</c></b>, whose screen sits beside
/// this one and writes the same row: that one maps GL accounts, this one sets behaviour, and the
/// live product keeps them on separate screens. Note the live General page also carries "VAT on
/// Purchase" and "VAT on Sales" account maps -- those are this codebase's
/// <c>DefaultVatReceivableAccountId</c>/<c>DefaultVatPayableAccountId</c> and already belong to the
/// accounting-defaults command, so they are deliberately not duplicated here.</para>
/// </summary>
public sealed record UpdateGeneralSettingsCommand(
    Guid OrganizationId,
    SuggestSellingPriceMode SuggestSellingPriceMode,
    ProductPriceBasis ProductPriceBasis,
    InventoryTrackingMode InventoryTrackingMode,
    BalanceAction NegativeCashBalanceAction,
    BalanceAction NegativeStockBalanceAction,
    BalanceAction CreditLimitExceedsAction)
    : IRequest<GeneralSettingsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.GeneralSettingsManage;
}
