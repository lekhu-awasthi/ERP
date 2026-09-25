using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Purchasing;

/// <summary>Phase 58 -- a Goods Received Note line as a client sends it. The
/// <see cref="PurchaseOrderLineInput"/> shape, including phase 52's optional unit (null = the
/// product's primary unit; the factor is resolved server-side and frozen, never accepted).</summary>
public sealed record GoodsReceivedNoteLineInput(
    Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct = 0,
    Guid? UnitId = null);
