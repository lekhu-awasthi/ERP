using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.CreatePosSale;

/// <summary>One line rung up at a till.</summary>
/// <param name="Rate">The price per unit before VAT -- the till prefills the product's selling price
/// and a cashier may change it (phase 62's line edit).</param>
/// <param name="VatRate">Null takes the product's own rate, which is what a till normally wants.</param>
public sealed record PosSaleLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    VatRate? VatRate = null,
    decimal DiscountPct = 0,
    Guid? UnitId = null,
    string? BatchNo = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>One way the customer paid: a payment mode the till offers, and the amount handed over in it.</summary>
public sealed record PosTenderInput(Guid PaymentModeId, decimal Amount);

/// <summary>
/// Phase 61 -- one till sale: an Invoice created <b>and approved</b> in one command (phase 59 Decision
/// C), its tenders posted as a second entry (Decision D), in the caller's open session at
/// <see cref="LocationId"/>.
///
/// <para><b>Permissions (Decision F of docs/phase-61-status.md).</b> The pipeline checks
/// <c>Sales.Invoice.Create</c> at the sale's location -- the key a Member already holds, scopable to
/// one branch by phase 32b. A fully paid sale needs nothing more: the till's control over the money is
/// the session, whose count is the cashier's accountability. <b>Part of the bill left on credit also
/// needs <c>Sales.Invoice.Approve</c> there</b>, re-checked in the handler because it depends on the
/// tenders, not the request's shape: a receivable is exactly what Approve exists to put a second pair
/// of eyes on, and it is the one thing a till sale creates that an ERP draft could not create without
/// that key.</para>
///
/// <para>The two warnings are phase 31's: a stock shortfall the tenant's setting warns about, and a
/// credit-limit breach, each a 422 with its own <c>warningKind</c> and its own override flag.</para>
/// </summary>
public sealed record CreatePosSaleCommand(
    Guid OrganizationId,
    Guid SessionId,
    Guid? LocationId,
    IReadOnlyList<PosSaleLineInput> Lines,
    IReadOnlyList<PosTenderInput> Tenders,
    decimal ChangeAmount = 0,
    Guid? ContactId = null,
    Guid? WarehouseId = null,
    PosTab? OrderType = null,
    decimal DiscountPct = 0,
    bool OverrideStockWarning = false,
    bool OverrideCreditLimitWarning = false)
    : IRequest<CreatePosSaleResult>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILockDateSensitive,
        IAuditableRequest, IMeteredTransaction, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.InvoiceCreate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;

    /// <summary>The Nepal date the sale is dated and posted on. Not bindable from a request body: a
    /// till sells now, and its date is what the day report groups by.</summary>
    public DateOnly Date { get; init; } = NepalTime.LocalDate(DateTimeOffset.UtcNow);

    public DocumentType AuditDocumentType => DocumentType.Invoice;

    /// <summary>A till sale is an approved Invoice, so it spends the same transaction allowance an
    /// ERP approve does (phase 41).</summary>
    public DocumentType MeteredDocumentType => DocumentType.Invoice;
}

/// <param name="CreditAmount">What was left on the customer's account.</param>
public sealed record CreatePosSaleResult(
    Guid Id,
    string Code,
    decimal GrandTotal,
    decimal ServiceCharge,
    decimal RoundOff,
    decimal Tendered,
    decimal ChangeAmount,
    decimal CreditAmount);
