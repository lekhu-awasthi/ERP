using ErpApp.Domain.Manufacturing;

namespace ErpApp.Domain.UnitTests.Manufacturing;

/// <summary>
/// The two production documents' lifecycles, and in particular the single-conversion gate the
/// reference product does not have (phase-6 bug #4: it still offered "Convert to Production
/// Journal" on PRO0011 after PJ0013 had been created from it).
/// </summary>
public class ProductionLifecycleTests
{
    private static ProductionOrder Order()
    {
        var order = ProductionOrder.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 2), null, Guid.NewGuid(), 10, null, null);
        order.AddRawMaterial(Guid.NewGuid(), 4);
        return order;
    }

    private static ProductionJournal Journal()
    {
        var journal = ProductionJournal.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 2), null, Guid.NewGuid(), 10, Guid.NewGuid(), null, null, null, null);
        journal.AddRawMaterial(Guid.NewGuid(), 4);
        return journal;
    }

    [Fact]
    public void A_production_order_is_created_in_draft_and_takes_its_code_at_approve()
    {
        var order = Order();

        Assert.Equal(ProductionOrderStatus.Draft, order.Status);
        Assert.Equal(ProductionOrder.DraftCode, order.Code);

        order.Approve(Guid.NewGuid(), "PRO0001");

        Assert.Equal(ProductionOrderStatus.Approved, order.Status);
        Assert.Equal("PRO0001", order.Code);
        Assert.NotNull(order.ApprovedAt);
    }

    [Fact]
    public void A_production_order_converts_exactly_once()
    {
        var order = Order();
        order.Approve(Guid.NewGuid(), "PRO0001");

        order.MarkConverted();
        Assert.Equal(ProductionOrderStatus.Converted, order.Status);

        // The whole reason the Converted member exists.
        Assert.Throws<InvalidOperationException>(order.MarkConverted);
    }

    [Fact]
    public void A_converted_production_order_cannot_be_voided()
    {
        var order = Order();
        order.Approve(Guid.NewGuid(), "PRO0001");
        order.MarkConverted();

        // Its live dependent is the journal created from it; voiding the plan underneath would
        // leave that journal pointing at a cancelled order.
        Assert.Throws<InvalidOperationException>(() => order.Void(Guid.NewGuid()));
    }

    [Fact]
    public void A_draft_production_order_cannot_be_converted()
    {
        var order = Order();
        Assert.Throws<InvalidOperationException>(order.MarkConverted);
    }

    [Fact]
    public void An_approved_production_order_can_no_longer_be_edited()
    {
        var order = Order();
        order.Approve(Guid.NewGuid(), "PRO0001");

        Assert.Throws<InvalidOperationException>(
            () => order.UpdateHeader(new DateOnly(2026, 9, 3), null, Guid.NewGuid(), 20, null, null));
        Assert.Throws<InvalidOperationException>(() => order.AddRawMaterial(Guid.NewGuid(), 1));
        Assert.Throws<InvalidOperationException>(order.ClearLines);
    }

    [Fact]
    public void A_production_order_needs_a_raw_material_to_be_approved()
    {
        var order = ProductionOrder.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 2), null, Guid.NewGuid(), 10, null, null);

        Assert.Throws<InvalidOperationException>(() => order.Approve(Guid.NewGuid(), "PRO0001"));
    }

    [Fact]
    public void A_production_journal_is_created_in_draft_and_takes_its_code_at_approve()
    {
        var journal = Journal();

        Assert.Equal(ProductionJournalStatus.Draft, journal.Status);
        Assert.Equal(ProductionJournal.DraftCode, journal.Code);

        journal.Approve(Guid.NewGuid(), "PJ0001");

        Assert.Equal(ProductionJournalStatus.Approved, journal.Status);
        Assert.Equal("PJ0001", journal.Code);
    }

    [Fact]
    public void Only_an_approved_production_journal_can_be_voided_and_only_once()
    {
        var journal = Journal();
        Assert.Throws<InvalidOperationException>(() => journal.Void(Guid.NewGuid()));

        journal.Approve(Guid.NewGuid(), "PJ0001");
        journal.Void(Guid.NewGuid());

        Assert.Equal(ProductionJournalStatus.Void, journal.Status);
        Assert.NotNull(journal.VoidedAt);
        Assert.Throws<InvalidOperationException>(() => journal.Void(Guid.NewGuid()));
    }

    [Fact]
    public void Output_quantity_must_be_positive_on_both_documents()
    {
        Assert.Throws<InvalidOperationException>(() => ProductionOrder.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 2), null, Guid.NewGuid(), 0, null, null));

        Assert.Throws<InvalidOperationException>(() => ProductionJournal.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 2), null, Guid.NewGuid(), -1, Guid.NewGuid(), null, null, null, null));
    }

    [Fact]
    public void A_by_product_percentage_must_be_at_least_zero_and_under_one_hundred()
    {
        var journal = Journal();

        Assert.Throws<InvalidOperationException>(() => journal.AddByProduct(Guid.NewGuid(), -1, 5));
        Assert.Throws<InvalidOperationException>(() => journal.AddByProduct(Guid.NewGuid(), 100, 5));
    }

    [Fact]
    public void A_bill_of_materials_derives_its_per_unit_ratios_from_the_output_quantity()
    {
        // The BOM read live on 2026-09-02: output 12, one raw material at 12 (ratio 1), one
        // by-product at 15 (ratio 1.25), one 500 expense (41.67 per unit).
        var bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 12, manufactureOnEverySale: false, null);
        bom.AddRawMaterial(Guid.NewGuid(), 12);
        bom.AddByProduct(Guid.NewGuid(), 12, 15);
        bom.AddExpense(Guid.NewGuid(), 500);

        Assert.Equal(1m, bom.RawMaterials[0].Quantity / bom.OutputQuantity);
        Assert.Equal(1.25m, bom.ByProducts[0].Quantity / bom.OutputQuantity);
        Assert.Equal(12m, bom.ByProducts[0].CostAllocationPct);
        bom.EnsureByProductAllocationIsSane();
    }
}
