namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// One input actually consumed by a <see cref="ProductionJournal"/>.
///
/// <para><b><see cref="ConsumedUnitCost"/>/<see cref="Amount"/> are stamped at Approve from what
/// IStockLedgerService.ConsumeAsync really returned, and are not user input.</b> The reference
/// product exposes an editable Rate here, pre-filled from stock cost (observed: 0.097341, six
/// decimals -- clearly derived). We deliberately do not: a user-entered rate lets the document's
/// stated cost diverge from the FIFO layers it actually consumed, which is the same reason
/// InventoryAdjustmentLine refuses to store a cost on a Decrease line, and the same precedent as
/// InvoiceLine.CogsUnitCost. A Draft shows a non-binding preview instead
/// (IStockLedgerService.PreviewConsumptionCostAsync).</para>
///
/// <para>Amount is stored rather than re-derived as Quantity x ConsumedUnitCost because
/// ConsumeAsync returns an unrounded weighted average whose stored form is rounded to the column's
/// scale: the multiplication has to happen on the unrounded value for the journal's Raw Material
/// Cost to equal, to the cent, what the ledger gave up. See docs/phase-25-status.md Decision B.</para>
/// </summary>
public sealed class ProductionJournalRawMaterialLine
{
    public Guid Id { get; private set; }
    public Guid ProductionJournalId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? ConsumedUnitCost { get; private set; }
    public decimal? Amount { get; private set; }

    private ProductionJournalRawMaterialLine()
    {
    }

    internal static ProductionJournalRawMaterialLine Create(Guid productionJournalId, Guid productId, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductionJournalId = productionJournalId,
            ProductId = productId,
            Quantity = quantity,
        };

    /// <summary>Called once, from ApproveProductionJournalCommandHandler, immediately after
    /// ConsumeAsync returns this line's actual weighted-average FIFO cost. Public for the same
    /// Domain/Application assembly-boundary reason InvoiceLine.RecordCogsUnitCost is.</summary>
    public void RecordConsumedCost(decimal consumedUnitCost, decimal amount)
    {
        ConsumedUnitCost = consumedUnitCost;
        Amount = amount;
    }
}

/// <summary>
/// One co-product created by a <see cref="ProductionJournal"/>. The percentage is user input; the
/// unit cost and amount are computed at Approve from the Total Cost of Production, and the unit
/// cost is what the by-product's own new FIFO layer is created at.
/// </summary>
public sealed class ProductionJournalByProductLine
{
    public Guid Id { get; private set; }
    public Guid ProductionJournalId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal CostAllocationPct { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? AllocatedUnitCost { get; private set; }
    public decimal? AllocatedAmount { get; private set; }

    private ProductionJournalByProductLine()
    {
    }

    internal static ProductionJournalByProductLine Create(
        Guid productionJournalId, Guid productId, decimal costAllocationPct, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductionJournalId = productionJournalId,
            ProductId = productId,
            CostAllocationPct = costAllocationPct,
            Quantity = quantity,
        };

    internal void RecordAllocatedCost(decimal allocatedUnitCost, decimal allocatedAmount)
    {
        AllocatedUnitCost = allocatedUnitCost;
        AllocatedAmount = allocatedAmount;
    }
}

/// <summary>One production expense of a <see cref="ProductionJournal"/>, naming a CostTerm whose
/// Category is ProductionCost. Amount is user input throughout -- unlike a raw material, no ledger
/// knows what the labour cost.</summary>
public sealed class ProductionJournalExpenseLine
{
    public Guid Id { get; private set; }
    public Guid ProductionJournalId { get; private set; }
    public Guid CostTermId { get; private set; }
    public decimal Amount { get; private set; }

    private ProductionJournalExpenseLine()
    {
    }

    internal static ProductionJournalExpenseLine Create(Guid productionJournalId, Guid costTermId, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductionJournalId = productionJournalId,
            CostTermId = costTermId,
            Amount = amount,
        };
}
