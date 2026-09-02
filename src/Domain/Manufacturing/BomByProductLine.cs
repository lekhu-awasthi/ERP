namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// One co-product of a <see cref="BillOfMaterials"/>. <see cref="CostAllocationPct"/> is a
/// percentage <b>of the Total Cost of Production</b> (raw material cost + production expenses) --
/// live-confirmed on 2026-09-02 against a real Production Journal: 12% of 501.168092 gave
/// 60.14017104 to the penny, and 5% of 800,000 gave 40,000. See docs/phase-25-status.md Decision C.
/// </summary>
public sealed class BomByProductLine
{
    public Guid Id { get; private set; }
    public Guid BillOfMaterialsId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal CostAllocationPct { get; private set; }
    public decimal Quantity { get; private set; }

    private BomByProductLine()
    {
    }

    internal static BomByProductLine Create(
        Guid billOfMaterialsId, Guid productId, decimal costAllocationPct, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            BillOfMaterialsId = billOfMaterialsId,
            ProductId = productId,
            CostAllocationPct = costAllocationPct,
            Quantity = quantity,
        };
}
