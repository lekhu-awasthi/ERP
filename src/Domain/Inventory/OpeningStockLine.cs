namespace ErpApp.Domain.Inventory;

/// <summary>
/// Phase 17 (Configurations §18, docs/phase-17-status.md) -- a "day-zero" per-product opening
/// stock quantity, one row per (OrganizationId, ProductId, WarehouseId). Category isn't stored
/// here -- the confirmed live screen's Category column is just the product's own
/// Catalog.Product.CategoryId shown by join, not a fact about the opening line itself.
///
/// Same no-lifecycle shape as OpeningBalanceLine: saving calls IStockLedgerService.IncrementAsync
/// directly (DocumentType.OpeningStock, this line's own Id as SourceDocumentId) to create a real
/// FIFO layer, so Stock Position needs no query change to see it. Editing an existing line first
/// calls IStockLedgerService.ReverseIncrementAsync -- which throws a 409 if the original layer has
/// already been partly consumed by a later real transaction, the same protection every other
/// document type's Void gets, not a new invariant.
/// </summary>
public sealed class OpeningStockLine
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OpeningStockLine()
    {
    }

    public static OpeningStockLine Create(Guid organizationId, Guid productId, Guid warehouseId, decimal quantity, decimal rate)
    {
        Validate(quantity, rate);

        var now = DateTimeOffset.UtcNow;
        return new OpeningStockLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            WarehouseId = warehouseId,
            Quantity = quantity,
            Rate = rate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(decimal quantity, decimal rate)
    {
        Validate(quantity, rate);
        Quantity = quantity;
        Rate = rate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void Validate(decimal quantity, decimal rate)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("An opening stock line's Quantity must be greater than zero.");
        }

        if (rate < 0)
        {
            throw new InvalidOperationException("An opening stock line's Rate cannot be negative.");
        }
    }
}
