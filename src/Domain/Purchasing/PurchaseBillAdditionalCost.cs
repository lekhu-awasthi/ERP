namespace ErpApp.Domain.Purchasing;

/// <summary>
/// One row of the Purchase Bill's <b>Additional Cost</b> section (FR-6.15, the landed-cost half of
/// <see cref="Configuration.CostTermCategory.AdditionalCost"/> that Phase 20c's lookup has been
/// waiting for since Phase 25 consumed only the ProductionCost half).
///
/// <para><b>The shape is exactly the live one</b>, confirmed 2026-09-04 on the reference product's
/// add form: <c>Cost Terms | Product | Method | Amount</c>, with the Product cell defaulting to
/// "All Product" (<see cref="ProductId"/> null) and Method to <see cref="AdditionalCostMethod.Value"/>.
/// <b>There is no payee field of any kind</b> -- a row names a Cost Term and nothing else, which is
/// half the reason the capitalisation credits a clearing account rather than anybody's payable
/// (see <see cref="PurchaseBill.AllocateAdditionalCosts"/>).</para>
///
/// <para>The live "Add product-wise" toggle does not change this record. With it off, a row is an
/// allocation <i>rule</i> ("300 of Freight, spread by value"); with it on, the section renders as a
/// product-by-cost-term matrix and every typed cell is simply a row that already names its product.
/// One shape serves both, which is why the toggle is stored as a display flag on the bill
/// (<see cref="PurchaseBill.IsProductWiseAdditionalCost"/>) rather than as a second entity.</para>
/// </summary>
public sealed class PurchaseBillAdditionalCost
{
    private readonly List<PurchaseBillAdditionalCostAllocation> _allocations = [];

    public Guid Id { get; private set; }
    public Guid PurchaseBillId { get; private set; }
    public Guid CostTermId { get; private set; }

    /// <summary>Null means the live picker's "All Product": spread across every goods line on the
    /// bill. Otherwise the one product this cost belongs to -- still spread by
    /// <see cref="Method"/>, because the same product may sit on more than one line.</summary>
    public Guid? ProductId { get; private set; }

    public AdditionalCostMethod Method { get; private set; }

    /// <summary>In the <i>document's</i> currency, like every other amount on the bill. The live
    /// column header reads "Amount (NPR)", but that tenant has a single-currency list, so the label
    /// is its base currency rather than evidence of a second denomination -- see
    /// docs/phase-29-status.md, Decision F.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What this row actually put on each goods line, written once at Approve by
    /// <see cref="PurchaseBill.AllocateAdditionalCosts"/>. Empty while the bill is a Draft.</summary>
    public IReadOnlyList<PurchaseBillAdditionalCostAllocation> Allocations => _allocations;

    private PurchaseBillAdditionalCost()
    {
    }

    internal static PurchaseBillAdditionalCost Create(
        Guid purchaseBillId, Guid costTermId, Guid? productId, AdditionalCostMethod method, decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("An additional cost needs a positive Amount.");
        }

        return new PurchaseBillAdditionalCost
        {
            Id = Guid.NewGuid(),
            PurchaseBillId = purchaseBillId,
            CostTermId = costTermId,
            ProductId = productId,
            Method = method,
            Amount = amount,
        };
    }

    internal PurchaseBillAdditionalCostAllocation Allocate(Guid purchaseBillLineId, decimal amount)
    {
        var allocation = PurchaseBillAdditionalCostAllocation.Create(Id, purchaseBillLineId, amount);
        _allocations.Add(allocation);
        return allocation;
    }
}
