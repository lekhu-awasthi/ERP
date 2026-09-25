using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseOrder;

public sealed record GetPurchaseOrderQuery(Guid OrganizationId, Guid Id)
    : IRequest<PurchaseOrderDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.PurchaseOrderView;

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
public sealed record PurchaseOrderLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount,
    Guid? UnitId, string? UnitName, decimal ConversionFactor);

public sealed record PurchaseOrderDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    string? Reference,
    PurchaseOrderStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    decimal DiscountPct,
    Guid? CustomStatusId,
    string? Terms,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
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
    // Phase 58 -- when a Goods Received Note was raised against this order, so the detail page can
    // stop offering Convert to Goods Received Note. Trailing and defaulted; a new field owes its
    // read path (phase 35a).
    DateTimeOffset? ReceivedAt = null);
