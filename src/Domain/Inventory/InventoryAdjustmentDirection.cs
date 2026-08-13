namespace ErpApp.Domain.Inventory;

/// <summary>Increase = stock take found more than on record / opening stock entry (creates a new
/// FIFO layer at UnitCost). Decrease = damage/write-off/shrinkage (consumes existing FIFO layers,
/// at whatever cost IStockLedgerService.Consume resolves -- UnitCost is not meaningful on a
/// Decrease line, see InventoryAdjustmentLine's doc comment).</summary>
public enum InventoryAdjustmentDirection
{
    Increase,
    Decrease,
}
