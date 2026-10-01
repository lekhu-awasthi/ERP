using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.PrintPosRefundReceipt;

/// <summary>
/// One printing of a till refund's credit note: the note exactly as it was issued, plus which printing
/// this is. It carries every particular VAT Rules 2053, Rule 20(1) asks of a credit note
/// (phase-63-status.md Decision A):
/// <list type="bullet">
/// <item>its serial number and date (<see cref="Code"/>, <see cref="Date"/>);</item>
/// <item>the supplier's name, address and registration number (<see cref="SellerName"/>,
/// <see cref="SellerAddress"/>, <see cref="SellerPan"/>);</item>
/// <item>the recipient's name and address, and registration number if registered
/// (<see cref="CustomerName"/>, <see cref="CustomerAddress"/>, <see cref="CustomerPan"/>) -- printed
/// whatever the sale's heading was, because an abbreviated sale omits the buyer and the note may not;</item>
/// <item>the number and date of the tax invoice it relates to (<see cref="InvoiceCode"/>,
/// <see cref="InvoiceDate"/>);</item>
/// <item>details of the goods or services and of the credit (<see cref="Lines"/>, <see cref="Reason"/>);</item>
/// <item>the amount of the credit and of its tax (<see cref="GrandTotal"/>, <see cref="Vat"/>).</item>
/// </list>
/// </summary>
/// <param name="PrintNumber">1 for the original; above 1 the note is a copy and says so (the 2072
/// procedure, §6, read to cover credit notes -- Decision A).</param>
public sealed record PosRefundReceiptDto(
    Guid CreditNoteId,
    string Code,
    int PrintNumber,
    DateTimeOffset PrintedAt,
    string PrintedByName,
    string SellerName,
    string? SellerAddress,
    string? SellerPan,
    bool SellerVatRegistered,
    string LocationName,
    DateOnly Date,
    DateTimeOffset? RefundedAt,
    string SessionCode,
    string CashierName,
    string? InvoiceCode,
    DateOnly? InvoiceDate,
    string CustomerName,
    string? CustomerAddress,
    string? CustomerPan,
    bool IsWalkIn,
    string? Reason,
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
    IReadOnlyList<PosReceiptTenderDto> Payouts,
    decimal PaidOut,
    decimal ToAccount);

/// <summary>
/// Phase 63 -- prints a till refund's credit note: records the printing in <c>CreditNotePrint</c>, then
/// returns the note carrying its print number. Every call is a printing, for phase 62 Decision B's
/// reason (a browser cannot tell a cancelled print dialog from a printed page).
///
/// <para><b>Permission: <c>Sales.CreditNote.View</c> at the note's location</b>, the receipt print's
/// rule turned to the note: printing it needs exactly what reading it needs.</para>
/// </summary>
public sealed record PrintPosRefundReceiptCommand(Guid OrganizationId, Guid CreditNoteId)
    : IRequest<PosRefundReceiptDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.CreditNoteView;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;

    public Guid LocationDocumentId => CreditNoteId;
}
