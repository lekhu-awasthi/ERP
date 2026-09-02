namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// One production expense of a <see cref="BillOfMaterials"/>, naming a
/// <c>CostTerm</c> whose Category is <c>ProductionCost</c> (Phase 20c built the lookup for exactly
/// this consumer; the reference product's own form header reads "Production Cost Terms"). Amount
/// is stated for the BOM's own OutputQuantity, and the "Amount/Unit" the reference product shows
/// is derived, on the same reasoning as BomRawMaterialLine's Qty/Unit.
/// </summary>
public sealed class BomExpenseLine
{
    public Guid Id { get; private set; }
    public Guid BillOfMaterialsId { get; private set; }
    public Guid CostTermId { get; private set; }
    public decimal Amount { get; private set; }

    private BomExpenseLine()
    {
    }

    internal static BomExpenseLine Create(Guid billOfMaterialsId, Guid costTermId, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            BillOfMaterialsId = billOfMaterialsId,
            CostTermId = costTermId,
            Amount = amount,
        };
}
