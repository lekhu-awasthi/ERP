namespace ErpApp.Application.Manufacturing;

/// <summary>One raw-material row of a Bill of Materials, Production Order or Production Journal
/// request. No rate: the journal's cost comes from the FIFO ledger at Approve, never from the
/// caller (see ProductionJournalRawMaterialLine).</summary>
public sealed record ProductionRawMaterialLineInput(Guid ProductId, decimal Quantity);

/// <summary>One by-product row. CostAllocationPct is a percentage of the Total Cost of Production
/// (docs/phase-25-status.md Decision C).</summary>
public sealed record ProductionByProductLineInput(Guid ProductId, decimal CostAllocationPct, decimal Quantity);

/// <summary>One production expense row, naming a CostTerm whose Category is ProductionCost.</summary>
public sealed record ProductionExpenseLineInput(Guid CostTermId, decimal Amount);
