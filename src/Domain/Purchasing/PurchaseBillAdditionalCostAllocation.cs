namespace ErpApp.Domain.Purchasing;

/// <summary>
/// What one <see cref="PurchaseBillAdditionalCost"/> row put on one <see cref="PurchaseBillLine"/>,
/// computed and stored at Approve. Together these rows are the product-by-cost-term matrix the
/// reference product renders on an approved bill's Overview (confirmed live 2026-09-04) -- which is
/// why the allocation is <i>persisted</i> rather than derived on the fly into the FIFO layer and
/// then forgotten: the live product shows the breakdown per (product, cost term), and a figure you
/// cannot show is a figure nobody can check.
///
/// <para>Denominated in the bill's own currency, like <see cref="PurchaseBillAdditionalCost.Amount"/>
/// itself; the base-currency fold happens on the way into the stock ledger and the general ledger,
/// never in storage (phase-28 Decision D).</para>
/// </summary>
public sealed class PurchaseBillAdditionalCostAllocation
{
    public Guid Id { get; private set; }
    public Guid PurchaseBillAdditionalCostId { get; private set; }
    public Guid PurchaseBillLineId { get; private set; }
    public decimal Amount { get; private set; }

    private PurchaseBillAdditionalCostAllocation()
    {
    }

    internal static PurchaseBillAdditionalCostAllocation Create(
        Guid purchaseBillAdditionalCostId, Guid purchaseBillLineId, decimal amount)
    {
        return new PurchaseBillAdditionalCostAllocation
        {
            Id = Guid.NewGuid(),
            PurchaseBillAdditionalCostId = purchaseBillAdditionalCostId,
            PurchaseBillLineId = purchaseBillLineId,
            Amount = amount,
        };
    }
}
