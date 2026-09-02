namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// One input of a <see cref="BillOfMaterials"/>. Quantity is stated for the BOM's own
/// OutputQuantity; the per-output-unit ratio the reference product displays as "Qty/Unit" is
/// derived (Quantity / OutputQuantity), never stored -- storing it would give the same fact two
/// homes that can disagree after an Output Quantity edit.
/// </summary>
public sealed class BomRawMaterialLine
{
    public Guid Id { get; private set; }
    public Guid BillOfMaterialsId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    private BomRawMaterialLine()
    {
    }

    internal static BomRawMaterialLine Create(Guid billOfMaterialsId, Guid productId, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            BillOfMaterialsId = billOfMaterialsId,
            ProductId = productId,
            Quantity = quantity,
        };
}
