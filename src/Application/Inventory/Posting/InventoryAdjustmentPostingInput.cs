namespace ErpApp.Application.Inventory.Posting;

/// <summary>
/// Pure input shape for InventoryAdjustmentPostingRule.BuildLines -- IncreaseAmount/DecreaseAmount
/// are each already-summed totals (in currency, not quantity) resolved by
/// ApproveInventoryAdjustmentCommandHandler before calling BuildLines: an Increase line's amount
/// is Quantity*UnitCost (the user-entered cost); a Decrease line's amount is
/// Quantity*IStockLedgerService.ConsumeAsync's returned weighted-average cost (the real FIFO cost
/// of what was actually consumed, not a user-entered figure -- see InventoryAdjustmentLine's doc
/// comment for why a Decrease line never carries its own UnitCost).
/// </summary>
public sealed record InventoryAdjustmentPostingInput(
    Guid InventoryAccountId, Guid AdjustmentAccountId, decimal IncreaseAmount, decimal DecreaseAmount);
