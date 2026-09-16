using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

/// <summary>
/// Phase 51 — the two tracking flags, and the one rule that makes them coherent: both are dimensions
/// <b>of the stock ledger</b>, so a product with no stock ledger cannot carry either.
///
/// <para>These are the Domain backstop. The validator says the same thing as a 400 naming the field,
/// because a Domain invariant reached through an endpoint is a 500 that tells the caller nothing
/// (phase 39) — and only an E2E ever sees that, since every handler test constructs a valid
/// command.</para>
/// </summary>
public class ProductTrackingFlagTests
{
    private static Product Goods(bool trackInventory = true, bool batch = false, bool serial = false) =>
        Product.Create(
            Guid.NewGuid(), ProductType.Goods, "Widget", "PRD-0001", Guid.NewGuid(), Guid.NewGuid(),
            null, true, 150m, 100m, VatRate.ThirteenPercentVat, 10, trackInventory,
            batchTracking: batch, serialTracking: serial);

    [Fact]
    public void Both_flags_are_off_by_default_so_the_feature_is_additive()
    {
        // The whole claim that a tenant who never turns them on sees no new control rests on this.
        var product = Goods();

        Assert.False(product.BatchTracking);
        Assert.False(product.SerialTracking);
    }

    [Fact]
    public void A_goods_product_tracking_inventory_may_carry_either_flag()
    {
        Assert.True(Goods(batch: true).BatchTracking);
        Assert.True(Goods(serial: true).SerialTracking);
    }

    [Fact]
    public void Both_flags_together_are_allowed()
    {
        // The reference product shows two independent toggles, so a serialised item belonging to a
        // batch is a shape the model has to admit -- under this model it is a layer of quantity one
        // that also carries a BatchId, which needs no special case anywhere.
        var product = Goods(batch: true, serial: true);

        Assert.True(product.BatchTracking);
        Assert.True(product.SerialTracking);
    }

    [Fact]
    public void A_service_product_cannot_be_batch_tracked()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Product.Create(
            Guid.NewGuid(), ProductType.Service, "Consulting", "PRD-0002", Guid.NewGuid(), Guid.NewGuid(),
            null, true, 0m, 0m, VatRate.NoVat, 0, false, batchTracking: true));

        Assert.Contains("Goods", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_goods_product_not_tracking_inventory_cannot_be_batch_tracked()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Goods(trackInventory: false, batch: true));

        Assert.Contains("Track Inventory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_goods_product_not_tracking_inventory_cannot_be_serial_tracked()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Goods(trackInventory: false, serial: true));

        Assert.Contains("Track Inventory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_enforces_the_same_rule_as_create()
    {
        // Turning Track Inventory off on a batch-tracked product is the half of the rule a user can
        // actually trip, and it is the half the update validator can check.
        var product = Goods(batch: true);

        Assert.Throws<InvalidOperationException>(() => product.Update(
            "Widget", product.CategoryId, product.PrimaryUnitId, null, true, 150m, 100m,
            VatRate.ThirteenPercentVat, 10, trackInventory: false, isActive: true,
            batchTracking: true, serialTracking: false));
    }

    [Fact]
    public void A_generated_variant_inherits_both_flags_from_its_parent()
    {
        // The flags are catalog metadata and are copied down exactly as TrackInventory already is.
        // A parent never holds a layer, so the flag on a parent is a template and nothing else --
        // what is refused is creating a ProductBatch for one, which ProductVariantRules does.
        var parent = Goods(batch: true, serial: true);

        var attributeId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        parent.SetVariantAttributeUsages([(attributeId, optionId)]);

        var variant = parent.CreateVariant(
            "PRD-0001-A", "Widget / A", [(attributeId, optionId)], 150m, 100m, null, null);

        Assert.True(variant.BatchTracking);
        Assert.True(variant.SerialTracking);

        // And the other half of the parent rule, asserted rather than believed: the parent is a
        // variant parent, which ProductVariantRules refuses on every document line -- so the only
        // path that can mint a batch cannot reach it. No phase-51 refusal was needed.
        Assert.True(parent.HasVariants);
    }
}
