using ErpApp.Application.Inventory.Stock;
using static ErpApp.Application.Inventory.Stock.IssuedStockRestock;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 93 -- how putting back what a document issued divides among its lines. The movements are per document, not
/// per line, so these pin the sharing: a named batch is taken first, a walked line takes what is left in the order it
/// was taken, a serial goes back under the batch it left from, and stock no movement covers goes back un-batched.
/// </summary>
public class IssuedStockRestockPlanTests
{
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid B1 = Guid.NewGuid();
    private static readonly Guid B2 = Guid.NewGuid();

    private static RestockPart Part(Guid? batch, decimal quantity, params string[] serials) => new(batch, quantity, serials);

    private static void Same(IReadOnlyList<RestockPart> expected, IReadOnlyList<RestockPart> actual) =>
        Assert.Equal(
            expected.Select(x => (x.BatchId, x.Quantity, string.Join(",", x.SerialNumbers))),
            actual.Select(x => (x.BatchId, x.Quantity, string.Join(",", x.SerialNumbers))));

    [Fact]
    public void A_named_line_takes_its_batch_first_and_a_walked_line_of_the_same_product_takes_the_rest()
    {
        // One sale: line 1 walked 4 (B1 3, then B2 1); line 2 named B1 for 2. The movements do not say which line took
        // which, so line 2 is given its B1 and line 1 what is left -- B1 1 and B2 1 -- whatever order the lines are in.
        IssuedRow[] issued = [new(Item, B1, null, 5m), new(Item, B2, null, 1m)];

        var plan = Plan([new RestockLine(Item, 4m, null, []), new RestockLine(Item, 2m, B1, [])], issued);

        Same([Part(B1, 3m), Part(B2, 1m)], plan[0]);
        Same([Part(B1, 2m)], plan[1]);
    }

    [Fact]
    public void A_product_without_batches_takes_one_part_per_line_as_it_always_did()
    {
        IssuedRow[] issued = [new(Other, null, null, 3m), new(Item, B1, null, 2m)];

        var plan = Plan([new RestockLine(Other, 2m, null, []), new RestockLine(Other, 1m, null, [])], issued);

        Same([Part(null, 2m)], plan[0]);
        Same([Part(null, 1m)], plan[1]);
    }

    [Fact]
    public void A_serial_goes_back_under_the_batch_it_left_from_and_uncovered_stock_goes_back_unbatched()
    {
        IssuedRow[] issued = [new(Item, B2, "SN-2", 1m), new(Item, B1, "SN-1", 1m), new(Item, B1, null, 1m)];

        var plan = Plan(
            [new RestockLine(Item, 2m, null, ["sn-1", "SN-2"]), new RestockLine(Item, 3m, null, [])], issued);

        Same([Part(B1, 1m, "sn-1"), Part(B2, 1m, "SN-2")], plan[0]);
        // One left in B1 that no serial line used; the other two predate phase 51's stamping, and go back in no batch.
        Same([Part(B1, 1m), Part(null, 2m)], plan[1]);
    }
}
