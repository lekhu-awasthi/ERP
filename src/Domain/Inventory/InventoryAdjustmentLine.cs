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

    /// <summary>Null until ApproveInventoryAdjustmentCommandHandler actually consumes FIFO stock
    /// for a Direction=Decrease line (an Increase line never gets one -- its cost is the
    /// user-entered UnitCost above). Set once, from IStockLedgerService.ConsumeAsync's actual
    /// weighted-average result -- mirrors InvoiceLine.CogsUnitCost's precedent -- so voiding this
    /// adjustment can put stock back at the exact cost it left at.</summary>
    public decimal? ConsumedUnitCost { get; private set; }

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

    /// <summary>Called once, from ApproveInventoryAdjustmentCommandHandler right after
    /// IStockLedgerService.ConsumeAsync returns this Decrease line's actual weighted-average cost.
    /// Public (not internal) for the same Domain/Application assembly-boundary reason InvoiceLine.
    /// RecordCogsUnitCost is public.</summary>
    public void RecordConsumedUnitCost(decimal unitCost) => ConsumedUnitCost = unitCost;
}
