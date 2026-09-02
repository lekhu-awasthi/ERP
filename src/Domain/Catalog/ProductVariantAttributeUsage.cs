namespace ErpApp.Domain.Catalog;

/// <summary>
/// One option a *parent* variant product has enabled -- the live reference product's "Attributes
/// Used" section, where a parent offering Color:{Red,Blue} and Size:{L,XL} defines the pool its
/// variants may be drawn from. Confirmed live: the pool is a subset of the tenant-global
/// <see cref="VariantAttributeOption"/> catalog, chosen per product.
///
/// The pool is deliberately NOT the set of variants: the live tenant's "Iphone 16 Pro Max" enables
/// 4 colours x 3 sizes (12 combinations) while carrying only 4 actual variants. Enabling an option
/// grants permission to create a variant; it does not create one.
/// </summary>
public sealed class ProductVariantAttributeUsage
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid VariantAttributeId { get; private set; }
    public Guid VariantAttributeOptionId { get; private set; }

    private ProductVariantAttributeUsage()
    {
    }

    internal static ProductVariantAttributeUsage Create(Guid productId, Guid variantAttributeId, Guid optionId)
    {
        return new ProductVariantAttributeUsage
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            VariantAttributeId = variantAttributeId,
            VariantAttributeOptionId = optionId,
        };
    }
}
