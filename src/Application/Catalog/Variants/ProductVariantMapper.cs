using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Catalog.Variants;

/// <summary>Shared projection so the variant panel, the generate command and the list query all
/// return one shape. Takes the attribute/option lookups the caller already loaded rather than
/// querying itself -- these run inside handlers that have just fetched the catalog anyway.</summary>
public static class ProductVariantMapper
{
    public static ProductVariantResult ToResult(
        Product variant,
        IReadOnlyDictionary<Guid, string> attributeNames,
        IReadOnlyDictionary<Guid, string> optionValues)
    {
        var values = variant.VariantValues
            .Select(v => new ProductVariantValueResult(
                v.VariantAttributeId,
                attributeNames.GetValueOrDefault(v.VariantAttributeId, string.Empty),
                v.VariantAttributeOptionId,
                optionValues.GetValueOrDefault(v.VariantAttributeOptionId, string.Empty)))
            .OrderBy(x => x.AttributeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProductVariantResult(
            variant.Id,
            variant.ParentProductId!.Value,
            variant.Code,
            variant.Name,
            variant.Sku,
            variant.Barcode,
            variant.SellingPrice,
            variant.PurchasePrice,
            variant.IsActive,
            values);
    }

    public static IReadOnlyList<ProductVariantAttributeUsageResult> ToUsageResults(
        Product parent,
        IReadOnlyDictionary<Guid, string> attributeNames,
        IReadOnlyDictionary<Guid, string> optionValues)
    {
        return parent.VariantAttributeUsages
            .GroupBy(x => x.VariantAttributeId)
            .Select(g => new ProductVariantAttributeUsageResult(
                g.Key,
                attributeNames.GetValueOrDefault(g.Key, string.Empty),
                g.Select(o => new ProductVariantOptionRef(
                        o.VariantAttributeOptionId, optionValues.GetValueOrDefault(o.VariantAttributeOptionId, string.Empty)))
                    .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(x => x.AttributeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Default variant name, matching the live product's own composition: the parent's
    /// name followed by each chosen option value ("Iphone 16 Pro Max XXL Blue"). Attribute order is
    /// the order the caller passed the combination in, which for generation is the order the
    /// attributes appear in the pool.</summary>
    public static string ComposeName(string parentName, IEnumerable<string> optionValues) =>
        string.Join(" ", new[] { parentName }.Concat(optionValues)).Trim();
}
