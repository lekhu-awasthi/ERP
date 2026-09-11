using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseBill;

public sealed record GetPurchaseBillQuery(Guid OrganizationId, Guid Id)
    : IRequest<PurchaseBillDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.PurchaseBillView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;
}

public sealed record PurchaseBillLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount,
    ExpenditureClassification ExpenditureClassification);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

/// <summary>Phase 29 (FR-6.15). One Additional Cost row as entered, with what it actually put on
/// each line once the bill was approved -- the two together are the product-by-cost-term matrix the
/// reference product renders on an approved bill.</summary>
public sealed record PurchaseBillAdditionalCostDto(
    Guid Id,
    Guid CostTermId,
    Guid? ProductId,
    AdditionalCostMethod Method,
    decimal Amount,
    IReadOnlyList<PurchaseBillAdditionalCostAllocationDto> Allocations);

public sealed record PurchaseBillAdditionalCostAllocationDto(Guid PurchaseBillLineId, decimal Amount);

public sealed record PurchaseBillDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    string Code,
    DateOnly Date,
    // Phase 31 -- the stored Due Date, so the detail page and its form can round-trip it.
    DateOnly DueDate,
    string? Reference,
    string? SupplierInvoiceReference,
    bool IsImport,
    string? ImportCountry,
    DateOnly? ImportDate,
    string? ImportDocumentNo,
    Guid? TdsTypeId,
    decimal TdsAmount,
    PurchaseBillStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal DiscountPct,
    decimal GrandTotal,
    IReadOnlyList<PurchaseBillLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate,
    // Phase 29 (FR-6.15) -- the Additional Cost section. AdditionalCostTotal is in CurrencyCode,
    // like the rows themselves, and is deliberately not part of GrandTotal (confirmed live). The two
    // capitalisation figures are in the base currency and are null until Approve.
    IReadOnlyList<PurchaseBillAdditionalCostDto> AdditionalCosts,
    bool IsProductWiseAdditionalCost,
    decimal AdditionalCostTotal,
    decimal? CapitalisedAdditionalCost,
    decimal? AdditionalCostRoundingAdjustment,
    // Phase 35 -- the billing location this document was raised from, null when the tenant's
    // LocationScopeMode excludes this type. Phase 32 put the column on all 17 types but the header
    // picker on Invoice alone, so only InvoiceDetailDto ever carried it back: a detail query
    // projecting a DTO silently drops a field the aggregate has, and the form would then post the
    // picker's default over a stored location on every edit. Fourteen instances of phase-32's own
    // carried gotcha.
    Guid? LocationId);
