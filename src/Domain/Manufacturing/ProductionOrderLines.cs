namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// One planned input of a <see cref="ProductionOrder"/>. <b>Quantity only, no rate</b> -- the
/// reference product's Raw Material table on this document has exactly two columns, because a
/// Production Order is an uncosted plan: nothing here touches stock, the general ledger or COGS.
/// The costing happens once, at the Production Journal's Approve, against the FIFO layers actually
/// walked.
/// </summary>
public sealed class ProductionOrderRawMaterialLine
{
    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    private ProductionOrderRawMaterialLine()
    {
    }

    internal static ProductionOrderRawMaterialLine Create(Guid productionOrderId, Guid productId, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = productionOrderId,
            ProductId = productId,
            Quantity = quantity,
        };
}

/// <summary>One planned co-product of a <see cref="ProductionOrder"/>. Carries the % of Cost the
/// plan intends, so converting to a Journal can default it; the plan itself never computes an
/// amount from it.</summary>
public sealed class ProductionOrderByProductLine
{
    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal CostAllocationPct { get; private set; }
    public decimal Quantity { get; private set; }

    private ProductionOrderByProductLine()
    {
    }

    internal static ProductionOrderByProductLine Create(
        Guid productionOrderId, Guid productId, decimal costAllocationPct, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = productionOrderId,
            ProductId = productId,
            CostAllocationPct = costAllocationPct,
            Quantity = quantity,
        };
}

/// <summary>One planned production expense of a <see cref="ProductionOrder"/>. This one <i>does</i>
/// carry an Amount even though the order is uncosted -- confirmed live, and consistent: an expense
/// is a figure the planner states, unlike a raw material's cost which only the ledger can
/// know.</summary>
public sealed class ProductionOrderExpenseLine
{
    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid CostTermId { get; private set; }
    public decimal Amount { get; private set; }

    private ProductionOrderExpenseLine()
    {
    }

    internal static ProductionOrderExpenseLine Create(Guid productionOrderId, Guid costTermId, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = productionOrderId,
            CostTermId = costTermId,
            Amount = amount,
        };
}
