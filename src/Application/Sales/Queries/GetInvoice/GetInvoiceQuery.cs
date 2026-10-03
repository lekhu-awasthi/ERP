using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetInvoice;

public sealed record GetInvoiceQuery(Guid OrganizationId, Guid Id)
    : IRequest<InvoiceDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.InvoiceView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;
}

/// <param name="BatchNo">Phase 51 -- the batch this line names, by number, or null. Read back as a
/// number rather than an id because that is what the form's one Item Batch control holds and what
/// the client would have to resolve otherwise. Phase 35a's rule is the reason this is here at all:
/// a detail DTO that drops a field the write path stored means the form can never show it, and that
/// phase found 14 of 15 detail reads doing exactly that.</param>
/// <param name="ManufactureDate">Phase 51 -- the named batch's own dates, so the form can show them
/// beside the number without a second round trip. Null when the line names no batch.</param>
/// <param name="ExpiryDate">See <paramref name="ManufactureDate"/>.</param>
/// <param name="SerialNumbers">Phase 51 -- the serial numbers this line names, one per physical
/// unit. Empty for every line of every product that is not serial-tracked.</param>
/// <param name="UnitId">Phase 52 -- the unit this line was entered in, null when it was the
/// product's own primary unit. Present so a form can round-trip the user's choice: phase 35a's
/// rule is that adding a field to many aggregates owes write, read and every prefill between,
/// and a detail DTO that drops it leaves the form unable to show what was saved.</param>
/// <param name="UnitName">The unit's short name (<c>BTL</c>), for rendering beside the quantity.</param>
/// <param name="ConversionFactor">How many primary units one of them was worth <b>when this line
/// was written</b>. Sent so a reader can see the frozen fact rather than infer it from a
/// catalogue that may since have changed. The converted quantity is deliberately not sent --
/// it is <c>Quantity x ConversionFactor</c>, and shipping it would put a second quantity on the
/// wire that can contradict the first.</param>
public sealed record InvoiceLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount,
    decimal VatAmount,
    string? BatchNo,
    DateOnly? ManufactureDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string> SerialNumbers,
    Guid? UnitId, string? UnitName, decimal ConversionFactor,
    // Phase 61 -- a till line's service charge rate and amount (zero on every ERP line). VatAmount
    // already includes the VAT on the service charge, which is inside the VAT base.
    decimal ServiceChargeRate, decimal ServiceChargeAmount);

/// <summary>Phase 61 -- one way a till sale was paid, as frozen on the invoice.</summary>
public sealed record InvoiceTenderDto(Guid PaymentModeId, string PaymentModeName, PaymentModeKind Kind, decimal Amount);

/// <summary>
/// Phase 61 -- what a till sale carries that an ERP invoice does not. Null on every ERP invoice. The
/// detail page shows it, because a POS sale opened in the ERP must show the same total the customer
/// paid: the page computes an ERP invoice's totals from its lines and knows nothing of service charge,
/// round-off or tenders (phase 35a's rule -- a field written is owed to every reader).
/// </summary>
public sealed record InvoicePosSaleDto(
    Guid? PosSessionId,
    string? SessionCode,
    PosTab? OrderType,
    decimal ServiceChargeTotal,
    decimal RoundOff,
    IReadOnlyList<InvoiceTenderDto> Tenders,
    decimal TenderedAmount,
    decimal ChangeAmount,
    decimal CreditAmount);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record InvoiceDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    // Phase 32 -- the billing location this invoice was raised from, null when the tenant's
    // LocationScopeMode excludes Invoice. Unlike the list (which returns the aggregate itself, so the
    // field came for free), this detail query projects an explicit DTO -- so omitting it here would
    // leave the header picker unable to show the stored value on an existing invoice while the write
    // path worked perfectly. Caught by the phase-32 E2E, not by any test.
    Guid? LocationId,
    string Code,
    DateOnly Date,
    // Phase 31 -- the stored Due Date, so the detail page and its form can round-trip it.
    DateOnly DueDate,
    string? Reference,
    bool IsExport,
    string? ExportCountry,
    string? ExportDeclarationNo,
    DateOnly? ExportDeclarationDate,
    InvoiceStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal DiscountPct,
    decimal GrandTotal,
    string? Terms,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate,
    // Phase 61 -- which front end raised it, and the till's own figures when that was the POS.
    SalesChannel Channel,
    InvoicePosSaleDto? PosSale,
    // Phase 67 -- how many copies have left the system, by any medium (till, PDF, email). The page shows
    // it beside Print, so whoever prints knows the next copy is marked "COPY OF ORIGINAL".
    int PrintCount);
