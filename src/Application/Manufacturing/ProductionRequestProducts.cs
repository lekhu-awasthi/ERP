namespace ErpApp.Application.Manufacturing;

/// <summary>
/// Every product id a manufacturing request names, in one list: the finished good plus every raw
/// material plus every by-product. Collected in one place so no handler can validate two of the
/// three and quietly skip the other -- which is exactly how a variant parent would slip past
/// ProductVariantRules and start accumulating stock nothing can sell (phase-24 Decision D).
/// </summary>
internal static class ProductionRequestProducts
{
    public static IReadOnlyList<Guid> Collect(
        Guid finishedProductId,
        IReadOnlyList<ProductionRawMaterialLineInput> rawMaterials,
        IReadOnlyList<ProductionByProductLineInput> byProducts)
    {
        var ids = new List<Guid> { finishedProductId };
        ids.AddRange(rawMaterials.Select(x => x.ProductId));
        ids.AddRange(byProducts.Select(x => x.ProductId));
        return ids.Distinct().ToList();
    }
}
