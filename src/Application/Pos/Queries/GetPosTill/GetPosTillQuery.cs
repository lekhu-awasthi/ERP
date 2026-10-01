using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosTill;

/// <summary>A payment mode this till takes: linked to it, active, and naming its account.</summary>
public sealed record PosTillPaymentModeDto(Guid Id, string Name, PaymentModeKind Kind);

/// <summary>A product category with at least one product sellable at this till.</summary>
public sealed record PosTillCategoryDto(Guid Id, string Name);

/// <summary>
/// Everything a till screen needs before its first sale, in one read.
/// </summary>
/// <param name="CanSellOnCredit">Whether the caller holds <c>Sales.Invoice.Approve</c> here, which a
/// sale leaving anything on credit needs (phase 61 Decision F). Read so the payment screen can say so
/// before Pay rather than after.</param>
/// <param name="IsVatRegistered">The seller's registration, which decides whether a bill is a tax
/// invoice at all (phase-62-status.md Decision A).</param>
/// <param name="PrintCreditNote">Phase 63 -- whether a refund's credit note prints itself, the location's
/// phase 60 toggle.</param>
/// <param name="CanRefund">Phase 63 -- whether the caller holds both <c>Sales.CreditNote.Create</c> and
/// <c>Sales.CreditNote.Approve</c> here, which every refund needs (phase-63-status.md Decision E). Read so
/// the till can say so before a cashier starts one.</param>
public sealed record PosTillDto(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    PosMode PosMode,
    IReadOnlyList<PosTab> AvailableTabs,
    PosTab? DefaultTab,
    bool ServiceChargeEnabled,
    decimal ServiceChargeRate,
    bool RoundOffEnabled,
    bool CashVerificationRequired,
    IReadOnlyList<int> Denominations,
    bool PrintInvoice,
    bool AbbreviatedTaxInvoiceEnabled,
    bool IsVatRegistered,
    Guid? WarehouseId,
    string? WarehouseName,
    PosWalkInCustomerDto? WalkInCustomer,
    IReadOnlyList<PosTillPaymentModeDto> PaymentModes,
    IReadOnlyList<PosTillCategoryDto> Categories,
    bool CanSellOnCredit,
    bool PrintCreditNote,
    bool CanRefund);

/// <summary>
/// Phase 62 -- the till's own read of its location. <c>GET /pos/locations/{id}/settings</c> already
/// returns most of this, but it is the Admin's configuration screen (<c>Pos.Settings.Manage</c>,
/// Admin-only), and a Member cashier must be able to run the till. So the till reads the subset it
/// acts on under <c>Pos.Session.Operate</c>, and nothing it does not: no accounts, nothing
/// editable (phase-62-status.md Decision D).
///
/// <para>The same refusals as every request acting at a till (<c>PosTill.LoadAsync</c>): a location
/// that is inactive, runs no till, or whose mode the tenant is no longer entitled to is a 409 here,
/// before the screen paints.</para>
/// </summary>
public sealed record GetPosTillQuery(Guid OrganizationId, Guid LocationId)
    : IRequest<PosTillDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
