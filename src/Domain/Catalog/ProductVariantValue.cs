namespace ErpApp.Domain.Catalog;

/// <summary>
/// One (attribute, option) pair of a *child* variant product's own combination -- "this product IS
/// Color:Blue". Distinct from <see cref="ProductVariantAttributeUsage"/>, which is the parent's
/// pool of offered options; this is a child's actual identity, one row per attribute.
///
/// Exists as a join row rather than living only inside the child's Name so a report or picker can
/// filter and group by attribute value ("every Blue variant") instead of substring-matching a
/// label. The combination is immutable after creation -- see Product.CombinationKey.
/// </summary>
public sealed class ProductVariantValue
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid VariantAttributeId { get; private set; }
    public Guid VariantAttributeOptionId { get; private set; }

    private ProductVariantValue()
    {
    }

    internal static ProductVariantValue Create(Guid productId, Guid variantAttributeId, Guid optionId)
    {
        return new ProductVariantValue
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            VariantAttributeId = variantAttributeId,
            VariantAttributeOptionId = optionId,
        };
    }
}
