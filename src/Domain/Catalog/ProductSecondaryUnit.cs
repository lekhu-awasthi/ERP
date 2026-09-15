namespace ErpApp.Domain.Catalog;

/// <summary>
/// Child entity of Product (architecture-spec.md §4.3) -- a multi-UOM row with its own conversion
/// rate + pricing, e.g. "Box" = 12 x primary unit "Piece" at its own selling/purchase price. Own
/// table, but no aggregate-root behavior of its own -- created only via Product.AddSecondaryUnit.
/// </summary>
public sealed class ProductSecondaryUnit
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal ConversionRate { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal PurchasePrice { get; private set; }

    private ProductSecondaryUnit()
    {
    }

    internal static ProductSecondaryUnit Create(
        Guid productId, Guid unitId, decimal conversionRate, decimal sellingPrice, decimal purchasePrice)
    {
        return new ProductSecondaryUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            ConversionRate = conversionRate,
            SellingPrice = sellingPrice,
            PurchasePrice = purchasePrice,
        };
    }

    /// <summary>
    /// Phase 45 -- the rate and the two prices are editable; <see cref="UnitId"/> is not.
    ///
    /// <para>The unit is this row's identity, not one of its values: <see cref="Product"/> refuses
    /// two rows for one unit, so "change the unit" is a move into another row's place rather than an
    /// edit of this one. Deleting and re-adding says that plainly; a settable UnitId would let one
    /// call silently collide with a row the caller never named.</para>
    /// </summary>
    internal void Update(decimal conversionRate, decimal sellingPrice, decimal purchasePrice)
    {
        ConversionRate = conversionRate;
        SellingPrice = sellingPrice;
        PurchasePrice = purchasePrice;
    }
}
