using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.PrintPosReceipt;

/// <summary>
/// The heading a receipt prints, decided by the seller and the bill (phase-62-status.md Decision A).
/// </summary>
public enum PosReceiptTitle
{
    /// <summary>The seller is not VAT-registered, so no bill of theirs is a tax invoice.</summary>
    Invoice = 1,

    /// <summary>The full tax invoice, VAT Rules Rule 17 (Schedule 5).</summary>
    TaxInvoice = 2,

    /// <summary>VAT Rules Rule 18 (Schedule 6): permitted, retail, at most Rs 10,000.</summary>
    AbbreviatedTaxInvoice = 3,
}

public sealed record PosReceiptLineDto(
    string ProductName,
    decimal Quantity,
    string UnitShortName,
    decimal Rate,
    decimal DiscountPct,
    decimal Amount,
    decimal ServiceChargeAmount,
    VatRate VatRate,
    decimal VatAmount,
    decimal LineTotal);

public sealed record PosReceiptTenderDto(string PaymentModeName, PaymentModeKind Kind, decimal Amount);

/// <summary>
/// One printing of a till sale's receipt: the bill exactly as it was issued, plus which printing this
/// is.
/// </summary>
/// <param name="PrintNumber">1 for the original. Above 1 the receipt is a copy and says so, with
/// this number as "printed N times" (Procedure Related to Computerized Invoicing, 2072, §6).</param>
/// <param name="GrossAmount">Quantity × rate over the lines, before any discount.</param>
/// <param name="DiscountAmount">What the line and bill discounts took off the gross.</param>
/// <param name="SubTotal">The lines' amounts after discount, before service charge and VAT.</param>
/// <param name="TaxableAmount">The amounts, with their service charge, of the lines charged VAT.</param>
/// <param name="NonTaxableAmount">The same of the lines charged none (exempt or zero-rated).</param>
public sealed record PosReceiptDto(
    Guid InvoiceId,
    string Code,
    PosReceiptTitle Title,
    int PrintNumber,
    DateTimeOffset PrintedAt,
    string PrintedByName,
    string SellerName,
    string? SellerAddress,
    string? SellerPan,
    string LocationName,
    DateOnly Date,
    DateTimeOffset? SoldAt,
    string SessionCode,
    string CashierName,
    PosTab? OrderType,
    string CustomerName,
    string? CustomerAddress,
    string? CustomerPan,
    bool IsWalkIn,
    IReadOnlyList<PosReceiptLineDto> Lines,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal SubTotal,
    decimal ServiceCharge,
    decimal TaxableAmount,
    decimal NonTaxableAmount,
    decimal Vat,
    decimal RoundOff,
    decimal GrandTotal,
    string AmountInWords,
    IReadOnlyList<PosReceiptTenderDto> Tenders,
    decimal Tendered,
    decimal ChangeAmount,
    decimal CreditAmount);

/// <summary>
/// Phase 62 -- prints a till sale's receipt: records the printing, then returns the receipt carrying
/// its print number. Every call is a printing, the first included, because the rule counts prints
/// and a browser cannot tell whether its print dialog was cancelled -- so a cancelled dialog still
/// counts, and the next print says "copy". That errs the safe way: a copy marked that the paper
/// trail can explain, never an unmarked second original (phase-62-status.md Decision B).
///
/// <para><b>Permission: <c>Sales.Invoice.View</c>, at the sale's location.</b> A receipt is the
/// invoice printed, so it needs exactly what reading that invoice needs, scoped by phase 32b through
/// <see cref="ILocationScopedDocument"/>. Not <c>Pos.Session.Operate</c> and not the session's owner:
/// a customer who returns tomorrow for a copy is served by whoever is at the till then.</para>
/// </summary>
public sealed record PrintPosReceiptCommand(Guid OrganizationId, Guid InvoiceId)
    : IRequest<PosReceiptDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.InvoiceView;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;

    public Guid LocationDocumentId => InvoiceId;
}
