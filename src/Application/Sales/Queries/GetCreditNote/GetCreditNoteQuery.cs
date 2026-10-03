using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetCreditNote;

public sealed record GetCreditNoteQuery(Guid OrganizationId, Guid Id)
    : IRequest<CreditNoteDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.CreditNoteView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;
}

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
public sealed record CreditNoteLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount,
    Guid? UnitId, string? UnitName, decimal ConversionFactor)
{
    /// <summary>Phase 63 -- the service charge a till refund line gives back; zero on an ERP line.</summary>
    public decimal ServiceChargeAmount { get; init; }
}

/// <summary>Phase 63 -- one way a till refund was paid back.</summary>
public sealed record CreditNotePayoutDto(
    Guid PaymentModeId, string PaymentModeName, ErpApp.Domain.Configuration.PaymentModeKind Kind, decimal Amount);

/// <summary>
/// Phase 63 -- what a till refund carries that an ERP credit note does not, for the detail page's
/// reason in <c>InvoicePosSaleDto</c>: a refund opened in the ERP must show the total it gave back
/// and how. Null on every ERP credit note.
/// </summary>
public sealed record CreditNotePosRefundDto(
    Guid? PosSessionId,
    string? SessionCode,
    string? Reason,
    decimal ServiceChargeTotal,
    decimal RoundOff,
    decimal GrandTotal,
    IReadOnlyList<CreditNotePayoutDto> Payouts,
    decimal PaidOutAmount,
    decimal ToAccountAmount);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record CreditNoteDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    string? Reference,
    CreditNoteStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal DiscountPct,
    string? Terms,
    IReadOnlyList<CreditNoteLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate,
    // Phase 35 -- the billing location this document was raised from, null when the tenant's
    // LocationScopeMode excludes this type. Phase 32 put the column on all 17 types but the header
    // picker on Invoice alone, so only InvoiceDetailDto ever carried it back: a detail query
    // projecting a DTO silently drops a field the aggregate has, and the form would then post the
    // picker's default over a stored location on every edit. Fourteen instances of phase-32's own
    // carried gotcha.
    Guid? LocationId,
    CreditNotePosRefundDto? PosRefund,
    // Phase 67 -- how many copies have left the system, by any medium; see InvoiceDetailDto.PrintCount.
    int PrintCount);
