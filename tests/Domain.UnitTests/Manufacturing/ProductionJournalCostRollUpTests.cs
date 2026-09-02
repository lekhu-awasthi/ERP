using ErpApp.Domain.Manufacturing;

namespace ErpApp.Domain.UnitTests.Manufacturing;

/// <summary>
/// <b>The conservation law, and the arithmetic behind it.</b>
///
/// <para>Every case here is built around one assertion: raw-material FIFO cost consumed +
/// production expenses = finished-goods value created + by-product value created (+ the named
/// rounding residue). An arithmetic error in ProductionJournal.ComputeAndRecordRollUp does not
/// surface as a crash -- it compounds silently through every future sale's COGS and lands in the
/// Income Statement months later -- so it is proven here rather than inferred.</para>
///
/// <para>The three-figure cases are taken from real approved journals in the reference tenant, read
/// live on 2026-09-02, so this suite is checking parity with observed behaviour rather than with
/// its own reasoning.</para>
/// </summary>
public class ProductionJournalCostRollUpTests
{
    private static ProductionJournal Journal(decimal outputQuantity) => ProductionJournal.Create(
        Guid.NewGuid(), new DateOnly(2026, 9, 2), null, Guid.NewGuid(), outputQuantity,
        Guid.NewGuid(), null, null, null, null);

    private static void Consume(ProductionJournal journal, decimal unitCost)
    {
        foreach (var line in journal.RawMaterials)
        {
            line.RecordConsumedCost(unitCost, line.Quantity * unitCost);
        }
    }

    private static void AssertConserves(ProductionJournal journal)
    {
        var valueIn = journal.RawMaterialCost!.Value + journal.ProductionExpenseCost!.Value;
        var valueOut = journal.FinishedGoodsCost!.Value + journal.CostAllocatedToByProduct!.Value;

        Assert.Equal(valueIn - valueOut, journal.CostRoundingAdjustment!.Value);
        Assert.Equal(valueIn, valueOut + journal.CostRoundingAdjustment!.Value);
    }

    [Fact]
    public void Value_in_equals_value_out_for_a_run_with_no_by_products_or_expenses()
    {
        var journal = Journal(10);
        journal.AddRawMaterial(Guid.NewGuid(), 4);
        Consume(journal, 25);

        journal.ComputeAndRecordRollUp();

        Assert.Equal(100m, journal.RawMaterialCost);
        Assert.Equal(0m, journal.ProductionExpenseCost);
        Assert.Equal(100m, journal.TotalCostOfProduction);
        Assert.Equal(0m, journal.CostAllocatedToByProduct);
        Assert.Equal(100m, journal.FinishedGoodsCost);
        Assert.Equal(10m, journal.FinishedGoodsUnitCost);
        Assert.Equal(0m, journal.CostRoundingAdjustment);
        AssertConserves(journal);
    }

    [Fact]
    public void Expenses_are_capitalised_into_the_finished_goods_unit_cost()
    {
        var journal = Journal(10);
        journal.AddRawMaterial(Guid.NewGuid(), 4);
        journal.AddExpense(Guid.NewGuid(), 40);
        Consume(journal, 25);

        journal.ComputeAndRecordRollUp();

        Assert.Equal(140m, journal.TotalCostOfProduction);
        Assert.Equal(140m, journal.FinishedGoodsCost);

        // The whole point of the document: the finished good enters stock at 14, not at the 10 the
        // raw material alone would give.
        Assert.Equal(14m, journal.FinishedGoodsUnitCost);
        AssertConserves(journal);
    }

    [Fact]
    public void A_by_product_takes_its_percentage_of_the_total_cost_of_production_and_the_finished_good_loses_it()
    {
        // Reference tenant's PJ0001, read live: raw 400,000, expenses 400,000, by-product at 5%,
        // 500 units produced, 125 units of by-product.
        var journal = Journal(500);
        journal.AddRawMaterial(Guid.NewGuid(), 1250);
        journal.AddExpense(Guid.NewGuid(), 375000);
        journal.AddExpense(Guid.NewGuid(), 25000);
        journal.AddByProduct(Guid.NewGuid(), 5, 125);
        Consume(journal, 320);

        journal.ComputeAndRecordRollUp();

        Assert.Equal(400000m, journal.RawMaterialCost);
        Assert.Equal(400000m, journal.ProductionExpenseCost);
        Assert.Equal(800000m, journal.TotalCostOfProduction);
        Assert.Equal(40000m, journal.CostAllocatedToByProduct);
        Assert.Equal(760000m, journal.FinishedGoodsCost);
        Assert.Equal(1520m, journal.FinishedGoodsUnitCost);
        Assert.Equal(320m, journal.ByProducts.Single().AllocatedUnitCost);
        Assert.Equal(0m, journal.CostRoundingAdjustment);
        AssertConserves(journal);
    }

    [Fact]
    public void Allocating_to_a_by_product_never_creates_value_from_nothing()
    {
        // The failure this test exists for: allocate a percentage to the by-product's new stock
        // layer but forget to reduce the finished good's, and total value out exceeds value in.
        var journal = Journal(100);
        journal.AddRawMaterial(Guid.NewGuid(), 50);
        journal.AddExpense(Guid.NewGuid(), 500);
        journal.AddByProduct(Guid.NewGuid(), 20, 25);
        journal.AddByProduct(Guid.NewGuid(), 10, 5);
        Consume(journal, 30);

        journal.ComputeAndRecordRollUp();

        Assert.Equal(2000m, journal.TotalCostOfProduction);
        Assert.Equal(600m, journal.CostAllocatedToByProduct);
        Assert.Equal(1400m, journal.FinishedGoodsCost);
        AssertConserves(journal);

        // And the by-products entered stock at exactly their allocation, not at some other figure.
        Assert.Equal(400m, journal.ByProducts[0].AllocatedAmount);
        Assert.Equal(200m, journal.ByProducts[1].AllocatedAmount);
    }

    [Fact]
    public void The_rounding_residue_is_reported_rather_than_hidden()
    {
        // The reference tenant's own PJ0006: 3250 rolled into 240 units. 3250/240 does not divide
        // evenly at four decimals, so the layer created is worth fractionally more than the cost
        // that went in -- and the roll-up says so instead of quietly absorbing it.
        var journal = Journal(240);
        journal.AddRawMaterial(Guid.NewGuid(), 1);
        Consume(journal, 3250);

        journal.ComputeAndRecordRollUp();

        Assert.Equal(13.5417m, journal.FinishedGoodsUnitCost);
        Assert.Equal(3250.008m, journal.FinishedGoodsCost);
        Assert.Equal(-0.008m, journal.CostRoundingAdjustment);
        AssertConserves(journal);

        // Bounded by OutputQuantity * 0.00005, as the aggregate's remarks claim.
        Assert.True(Math.Abs(journal.CostRoundingAdjustment!.Value) <= 240m * 0.00005m);
    }

    [Fact]
    public void Raw_material_cost_is_the_sum_of_what_each_line_actually_consumed_not_a_single_rate()
    {
        // Two materials at different costs: the roll-up must add real per-line amounts, not
        // multiply one rate by a total quantity.
        var journal = Journal(10);
        journal.AddRawMaterial(Guid.NewGuid(), 3);
        journal.AddRawMaterial(Guid.NewGuid(), 7);

        journal.RawMaterials[0].RecordConsumedCost(100m, 300m);
        journal.RawMaterials[1].RecordConsumedCost(20m, 140m);

        journal.ComputeAndRecordRollUp();

        Assert.Equal(440m, journal.RawMaterialCost);
        Assert.Equal(44m, journal.FinishedGoodsUnitCost);
        AssertConserves(journal);
    }

    [Fact]
    public void The_roll_up_refuses_to_run_before_the_fifo_costs_have_been_recorded()
    {
        var journal = Journal(10);
        journal.AddRawMaterial(Guid.NewGuid(), 4);

        // A roll-up computed from an uncosted line would silently report a zero raw-material cost,
        // which is the quietest possible way to create value from nothing.
        Assert.Throws<InvalidOperationException>(journal.ComputeAndRecordRollUp);
    }

    [Fact]
    public void By_products_may_not_be_allocated_the_whole_cost_of_production()
    {
        var journal = Journal(10);
        journal.AddRawMaterial(Guid.NewGuid(), 4);
        journal.AddByProduct(Guid.NewGuid(), 60, 5);
        journal.AddByProduct(Guid.NewGuid(), 40, 5);

        // At 100% the finished good enters stock at zero cost, and a zero-cost FIFO layer makes
        // every future sale of it 100% margin.
        var error = Assert.Throws<InvalidOperationException>(journal.EnsureByProductAllocationIsSane);
        Assert.Contains("100", error.Message, StringComparison.Ordinal);
    }
}
