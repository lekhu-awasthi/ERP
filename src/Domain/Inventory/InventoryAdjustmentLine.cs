namespace ErpApp.Domain.Inventory;

/// <summary>
/// Child line of InventoryAdjustment. UnitCost is required and meaningful only for a Direction=
/// Increase line (there's no existing FIFO layer to derive a cost from -- the user states what the
/// found/corrected-in stock is worth, same as a PurchaseBill line's Rate). A Direction=Decrease
/// line carries UnitCost=0; its real cost is resolved at Approve time from whichever existing FIFO
/// layers IStockLedgerService.Consume actually walks -- storing a user-entered cost on a Decrease
/// line would let it diverge from the FIFO layers it's actually consuming, corrupting the ledger.
/// </summary>
public sealed class InventoryAdjustmentLine
{
    public Guid Id { get; private set; }
    public Guid InventoryAdjustmentId { get; private set; }
    public Guid ProductId { get; private set; }
    public InventoryAdjustmentDirection Direction { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }

    private InventoryAdjustmentLine()
    {
    }

    internal static InventoryAdjustmentLine Create(
        Guid inventoryAdjustmentId, Guid productId, InventoryAdjustmentDirection direction, decimal quantity, decimal unitCost)
    {
        return new InventoryAdjustmentLine
        {
            Id = Guid.NewGuid(),
            InventoryAdjustmentId = inventoryAdjustmentId,
            ProductId = productId,
            Direction = direction,
            Quantity = quantity,
            UnitCost = direction == InventoryAdjustmentDirection.Increase ? unitCost : 0,
        };
    }
}
