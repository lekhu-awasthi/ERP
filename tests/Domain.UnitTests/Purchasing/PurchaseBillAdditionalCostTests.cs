using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;

namespace ErpApp.Domain.UnitTests.Purchasing;

/// <summary>
/// Phase 29 (FR-6.15) -- the allocation half of landed cost, tested where it is pure: no database,
/// no currency, no stock ledger. The conservation of each row's own Amount is the property that
/// matters here (the layer-value half is asserted in ApprovePurchaseBillCommandHandlerTests and
/// again in SQL during the manual E2E).
/// </summary>
public class PurchaseBillAdditionalCostTests
{
    [Fact]
    public void Value_method_spreads_pro_rata_by_line_amount()
    {
        var (bill, goods) = BillWithTwoGoodsLines(quantityA: 10m, rateA: 600m, quantityB: 5m, rateB: 120m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 660m);

        bill.AllocateAdditionalCosts(goods);

        // 6,000 and 600 -- a 10:1 value split, so 600 and 60.
        Assert.Equal(600m, bill.AllocatedAdditionalCostFor(bill.Lines[0].Id));
        Assert.Equal(60m, bill.AllocatedAdditionalCostFor(bill.Lines[1].Id));
    }

    [Fact]
    public void Quantity_method_spreads_pro_rata_by_line_quantity_not_value()
    {
        var (bill, goods) = BillWithTwoGoodsLines(quantityA: 10m, rateA: 600m, quantityB: 5m, rateB: 120m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Quantity, 660m);

        bill.AllocateAdditionalCosts(goods);

        // 10 and 5 units -- a 2:1 quantity split, which the 10:1 value ratio has no say in.
        Assert.Equal(440m, bill.AllocatedAdditionalCostFor(bill.Lines[0].Id));
        Assert.Equal(220m, bill.AllocatedAdditionalCostFor(bill.Lines[1].Id));
    }

    [Fact]
    public void A_row_naming_one_product_lands_entirely_on_that_product()
    {
        var (bill, goods) = BillWithTwoGoodsLines(quantityA: 10m, rateA: 600m, quantityB: 5m, rateB: 120m);
        var secondProductId = bill.Lines[1].ProductId;
        bill.AddAdditionalCost(Guid.NewGuid(), secondProductId, AdditionalCostMethod.Value, 500m);

        bill.AllocateAdditionalCosts(goods);

        Assert.Equal(0m, bill.AllocatedAdditionalCostFor(bill.Lines[0].Id));
        Assert.Equal(500m, bill.AllocatedAdditionalCostFor(bill.Lines[1].Id));
    }

    [Fact]
    public void Every_rows_amount_is_conserved_exactly_even_when_it_does_not_divide()
    {
        // 100 across three equal lines is 33.3333 each at the allocation scale, which cannot sum to
        // 100 -- the last line takes the remainder, so the row is still conserved to the paisa.
        var bill = NewBill();
        var productId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            bill.AddLine(productId, 1m, 100m, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        }

        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 100m);
        bill.AllocateAdditionalCosts(new HashSet<Guid> { productId });

        var allocations = bill.AdditionalCosts.Single().Allocations;
        Assert.Equal(3, allocations.Count);
        Assert.Equal(100m, allocations.Sum(x => x.Amount));
        Assert.Equal(33.3333m, allocations[0].Amount);
        Assert.Equal(33.3334m, allocations[2].Amount);
    }

    [Fact]
    public void Several_rows_accumulate_on_the_same_line()
    {
        var (bill, goods) = BillWithTwoGoodsLines(quantityA: 10m, rateA: 600m, quantityB: 5m, rateB: 120m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 660m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Quantity, 660m);

        bill.AllocateAdditionalCosts(goods);

        Assert.Equal(600m + 440m, bill.AllocatedAdditionalCostFor(bill.Lines[0].Id));
        Assert.Equal(60m + 220m, bill.AllocatedAdditionalCostFor(bill.Lines[1].Id));
        Assert.Equal(1320m, bill.AdditionalCostTotal);
    }

    [Fact]
    public void A_service_line_carries_no_additional_cost()
    {
        // The reference product offers service lines in its picker (confirmed live 2026-09-04); we
        // do not, because a service line creates no FIFO layer for the cost to live in. "All
        // Product" therefore means all *goods* lines, and the service line here gets nothing while
        // the goods line takes the whole amount.
        var bill = NewBill();
        var goodsProductId = Guid.NewGuid();
        var serviceProductId = Guid.NewGuid();
        bill.AddLine(goodsProductId, 2m, 100m, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        bill.AddLine(serviceProductId, 1m, 900m, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 50m);

        bill.AllocateAdditionalCosts(new HashSet<Guid> { goodsProductId });

        Assert.Equal(50m, bill.AllocatedAdditionalCostFor(bill.Lines[0].Id));
        Assert.Equal(0m, bill.AllocatedAdditionalCostFor(bill.Lines[1].Id));
    }

    [Fact]
    public void A_row_naming_a_service_product_is_rejected_rather_than_silently_dropped()
    {
        var bill = NewBill();
        var goodsProductId = Guid.NewGuid();
        var serviceProductId = Guid.NewGuid();
        bill.AddLine(goodsProductId, 2m, 100m, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        bill.AddLine(serviceProductId, 1m, 900m, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        bill.AddAdditionalCost(Guid.NewGuid(), serviceProductId, AdditionalCostMethod.Value, 50m);

        Assert.Throws<InvalidOperationException>(
            () => bill.AllocateAdditionalCosts(new HashSet<Guid> { goodsProductId }));
    }

    [Fact]
    public void An_additional_cost_on_a_bill_with_no_goods_lines_is_rejected()
    {
        var bill = NewBill();
        var serviceProductId = Guid.NewGuid();
        bill.AddLine(serviceProductId, 1m, 900m, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 50m);

        Assert.Throws<InvalidOperationException>(() => bill.AllocateAdditionalCosts(new HashSet<Guid>()));
    }

    [Fact]
    public void A_zero_or_negative_amount_is_refused_at_the_row()
    {
        var bill = NewBill();

        Assert.Throws<InvalidOperationException>(
            () => bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 0m));
        Assert.Throws<InvalidOperationException>(
            () => bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, -1m));
    }

    [Fact]
    public void Additional_cost_is_not_part_of_the_grand_total()
    {
        // Confirmed live: the reference product's Sub Total and Grand Total both exclude it, which
        // is what makes it a cost to capitalise rather than a larger payable to the supplier.
        var (bill, _) = BillWithTwoGoodsLines(quantityA: 10m, rateA: 600m, quantityB: 5m, rateB: 120m);
        bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 660m);

        Assert.Equal(6600m, bill.GrandTotal);
        Assert.Equal(660m, bill.AdditionalCostTotal);
    }

    [Fact]
    public void The_section_cannot_be_changed_once_the_bill_is_approved()
    {
        var (bill, _) = BillWithTwoGoodsLines(quantityA: 1m, rateA: 100m, quantityB: 1m, rateB: 100m);
        bill.Approve(Guid.NewGuid(), "PB0001");

        Assert.Throws<InvalidOperationException>(
            () => bill.AddAdditionalCost(Guid.NewGuid(), null, AdditionalCostMethod.Value, 10m));
        Assert.Throws<InvalidOperationException>(() => bill.ClearAdditionalCosts());
        Assert.Throws<InvalidOperationException>(() => bill.SetProductWiseAdditionalCost(true));
    }

    private static (PurchaseBill Bill, HashSet<Guid> GoodsProductIds) BillWithTwoGoodsLines(
        decimal quantityA, decimal rateA, decimal quantityB, decimal rateB)
    {
        var bill = NewBill();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        bill.AddLine(productA, quantityA, rateA, VatRate.NoVat, ExpenditureClassification.Others, 0m);
        bill.AddLine(productB, quantityB, rateB, VatRate.NoVat, ExpenditureClassification.Others, 0m);

        return (bill, [productA, productB]);
    }

    private static PurchaseBill NewBill() => PurchaseBill.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow),
        null, null, false, null, null, null, null, 0m, null, null);
}
