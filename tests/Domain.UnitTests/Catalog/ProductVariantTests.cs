using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

public class ProductVariantTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ColorId = Guid.NewGuid();
    private static readonly Guid SizeId = Guid.NewGuid();
    private static readonly Guid Red = Guid.NewGuid();
    private static readonly Guid Blue = Guid.NewGuid();
    private static readonly Guid Large = Guid.NewGuid();

    private static Product Parent(params (Guid AttributeId, Guid OptionId)[] pool)
    {
        var product = Product.Create(
            OrgId, ProductType.Goods, "T-Shirt", "P-0001", Guid.NewGuid(), Guid.NewGuid(), "6109.10",
            availableForSale: true, sellingPrice: 500m, purchasePrice: 300m, VatRate.ThirteenPercentVat,
            reOrderLevel: 5, trackInventory: true);

        product.SetAccounts(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        if (pool.Length > 0)
        {
            product.SetVariantAttributeUsages(pool);
        }

        return product;
    }

    [Fact]
    public void An_ordinary_product_is_neither_a_parent_nor_a_child()
    {
        var product = Parent();

        Assert.False(product.HasVariants);
        Assert.Null(product.ParentProductId);
        Assert.Null(product.CombinationKey);
        Assert.Empty(product.VariantValues);
    }

    [Fact]
    public void Offering_attribute_options_promotes_the_product_to_a_parent()
    {
        var product = Parent((ColorId, Red), (SizeId, Large));

        Assert.True(product.HasVariants);
        Assert.Equal(2, product.VariantAttributeUsages.Count);
    }

    [Fact]
    public void CreateVariant_inherits_everything_that_must_agree_and_overrides_only_its_own_identity()
    {
        var parent = Parent((ColorId, Red), (SizeId, Large));

        var variant = parent.CreateVariant(
            "P-0002", "T-Shirt Large Red", [(ColorId, Red), (SizeId, Large)], 550m, 320m, "SKU-1", "BAR-1");

        // Its own identity.
        Assert.Equal(parent.Id, variant.ParentProductId);
        Assert.Equal("P-0002", variant.Code);
        Assert.Equal("T-Shirt Large Red", variant.Name);
        Assert.Equal("SKU-1", variant.Sku);
        Assert.Equal("BAR-1", variant.Barcode);
        Assert.Equal(550m, variant.SellingPrice);
        Assert.Equal(320m, variant.PurchasePrice);
        Assert.False(variant.HasVariants);

        // Everything that must agree for the two to be one matrix.
        Assert.Equal(parent.Type, variant.Type);
        Assert.Equal(parent.CategoryId, variant.CategoryId);
        Assert.Equal(parent.PrimaryUnitId, variant.PrimaryUnitId);
        Assert.Equal(parent.VatRate, variant.VatRate);
        Assert.Equal(parent.HsCode, variant.HsCode);
        Assert.Equal(parent.ValuationMethod, variant.ValuationMethod);
        Assert.Equal(parent.TrackInventory, variant.TrackInventory);
        Assert.Equal(parent.SalesAccountId, variant.SalesAccountId);
        Assert.Equal(parent.PurchaseAccountId, variant.PurchaseAccountId);
        Assert.Equal(parent.SalesReturnAccountId, variant.SalesReturnAccountId);
        Assert.Equal(parent.PurchaseReturnAccountId, variant.PurchaseReturnAccountId);
    }

    [Fact]
    public void CreateVariant_records_one_value_per_attribute()
    {
        var parent = Parent((ColorId, Red), (SizeId, Large));

        var variant = parent.CreateVariant("P-0002", "n", [(ColorId, Red), (SizeId, Large)], 1m, 1m, null, null);

        Assert.Equal(2, variant.VariantValues.Count);
        Assert.Contains(variant.VariantValues, v => v.VariantAttributeId == ColorId && v.VariantAttributeOptionId == Red);
        Assert.Contains(variant.VariantValues, v => v.VariantAttributeId == SizeId && v.VariantAttributeOptionId == Large);
    }

    [Fact]
    public void CombinationKey_is_order_independent()
    {
        var a = Product.BuildCombinationKey([(ColorId, Red), (SizeId, Large)]);
        var b = Product.BuildCombinationKey([(SizeId, Large), (ColorId, Red)]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CombinationKey_differs_between_different_combinations()
    {
        var redLarge = Product.BuildCombinationKey([(ColorId, Red), (SizeId, Large)]);
        var blueLarge = Product.BuildCombinationKey([(ColorId, Blue), (SizeId, Large)]);

        Assert.NotEqual(redLarge, blueLarge);
    }

    [Fact]
    public void CreateVariant_rejects_an_option_the_parent_does_not_offer()
    {
        var parent = Parent((ColorId, Red));

        Assert.Throws<InvalidOperationException>(
            () => parent.CreateVariant("P-0002", "n", [(ColorId, Blue)], 1m, 1m, null, null));
    }

    [Fact]
    public void CreateVariant_rejects_two_values_of_the_same_attribute()
    {
        var parent = Parent((ColorId, Red), (ColorId, Blue));

        Assert.Throws<InvalidOperationException>(
            () => parent.CreateVariant("P-0002", "n", [(ColorId, Red), (ColorId, Blue)], 1m, 1m, null, null));
    }

    [Fact]
    public void CreateVariant_rejects_an_empty_combination()
    {
        var parent = Parent((ColorId, Red));

        Assert.Throws<InvalidOperationException>(
            () => parent.CreateVariant("P-0002", "n", [], 1m, 1m, null, null));
    }

    [Fact]
    public void A_variant_cannot_itself_have_variants()
    {
        var parent = Parent((ColorId, Red));
        var variant = parent.CreateVariant("P-0002", "n", [(ColorId, Red)], 1m, 1m, null, null);

        Assert.Throws<InvalidOperationException>(variant.MarkHasVariants);
        Assert.Throws<InvalidOperationException>(() => variant.SetVariantAttributeUsages([(SizeId, Large)]));
        Assert.Throws<InvalidOperationException>(
            () => variant.CreateVariant("P-0003", "n", [(ColorId, Red)], 1m, 1m, null, null));
    }

    [Fact]
    public void SetVariantAttributeUsages_diffs_rather_than_clearing_and_re_adding()
    {
        // CLAUDE.md's full-collection-replace gotcha (phase-4 bug #1): a same-count Clear+re-Add
        // mis-tracks under the InMemory provider. The rows that survive a change must be the SAME
        // instances, which is what proves a diff happened rather than a wholesale replace.
        var product = Parent((ColorId, Red), (SizeId, Large));
        var keptRow = product.VariantAttributeUsages.Single(x => x.VariantAttributeOptionId == Red);

        product.SetVariantAttributeUsages([(ColorId, Red), (ColorId, Blue)]);

        Assert.Same(keptRow, product.VariantAttributeUsages.Single(x => x.VariantAttributeOptionId == Red));
        Assert.DoesNotContain(product.VariantAttributeUsages, x => x.VariantAttributeOptionId == Large);
        Assert.Contains(product.VariantAttributeUsages, x => x.VariantAttributeOptionId == Blue);
    }

    [Fact]
    public void Clearing_the_pool_demotes_the_parent_back_to_an_ordinary_product()
    {
        var product = Parent((ColorId, Red));

        product.SetVariantAttributeUsages([]);

        Assert.False(product.HasVariants);
        Assert.Empty(product.VariantAttributeUsages);
    }

    [Fact]
    public void A_variants_prices_cannot_be_negative()
    {
        var parent = Parent((ColorId, Red));

        Assert.Throws<InvalidOperationException>(
            () => parent.CreateVariant("P-0002", "n", [(ColorId, Red)], -1m, 1m, null, null));
        Assert.Throws<InvalidOperationException>(
            () => parent.CreateVariant("P-0002", "n", [(ColorId, Red)], 1m, -1m, null, null));
    }

    [Fact]
    public void Blank_sku_and_barcode_normalize_to_null_rather_than_empty_strings()
    {
        var parent = Parent((ColorId, Red));

        var variant = parent.CreateVariant("P-0002", "n", [(ColorId, Red)], 1m, 1m, "   ", "");

        Assert.Null(variant.Sku);
        Assert.Null(variant.Barcode);
    }
}
