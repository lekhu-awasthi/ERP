namespace ErpApp.Application.Manufacturing.Posting;

/// <summary>
/// Pure input shape for <see cref="ProductionJournalPostingRule"/>. Every amount here is a value
/// that has <i>already happened</i> to the stock ledger by the time the rule runs, resolved by
/// ApproveProductionJournalCommandHandler:
///
/// <list type="bullet">
/// <item><see cref="RawMaterialCost"/> -- the sum of what IStockLedgerService.ConsumeAsync actually
/// removed from FIFO layers, never a user-entered or BOM-planned rate.</item>
/// <item><see cref="FinishedGoodsValue"/> / <see cref="ByProductValue"/> -- the values of the
/// layers IncrementAsync actually created, i.e. rounded unit cost times quantity, not the
/// theoretical roll-up figures. Using the theoretical ones would let the GL and the ledger drift
/// apart by the rounding residue, which is the single divergence this design exists to prevent.</item>
/// </list>
/// </summary>
public sealed record ProductionJournalPostingInput(
    Guid InventoryAccountId,
    Guid ProductionCostAccountId,
    decimal RawMaterialCost,
    decimal FinishedGoodsValue,
    decimal ByProductValue);
