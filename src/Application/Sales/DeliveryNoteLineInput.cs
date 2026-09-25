using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Sales;

/// <summary>Phase 58 -- a Delivery Note line as a client sends it: the Sales Order line shape with
/// phase 52's optional unit (null = the product's primary unit; the factor is resolved server-side
/// and frozen, never accepted).</summary>
public sealed record DeliveryNoteLineInput(
    Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct = 0,
    Guid? UnitId = null);
