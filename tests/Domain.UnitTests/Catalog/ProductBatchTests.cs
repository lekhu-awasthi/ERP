using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

/// <summary>
/// Phase 51 — the batch identity's own invariants. There are few, deliberately: a batch stores no
/// quantity, so almost everything that could be wrong about one is wrong about the ledger instead.
/// </summary>
public class ProductBatchTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void A_batch_trims_its_number_and_keeps_both_dates_optional()
    {
        var batch = ProductBatch.Create(OrganizationId, ProductId, "  BATCH123  ", null, null);

        Assert.Equal("BATCH123", batch.BatchNo);
        Assert.Null(batch.ManufactureDate);
        Assert.Null(batch.ExpiryDate);
    }

    [Fact]
    public void A_batch_needs_a_number()
    {
        Assert.Throws<InvalidOperationException>(
            () => ProductBatch.Create(OrganizationId, ProductId, "   ", null, null));
    }

    [Fact]
    public void A_batch_cannot_expire_before_it_was_manufactured()
    {
        Assert.Throws<InvalidOperationException>(() => ProductBatch.Create(
            OrganizationId, ProductId, "B1", new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void A_later_receipt_may_fill_a_date_the_first_one_left_blank()
    {
        // The goods arrive, the paperwork follows.
        var batch = ProductBatch.Create(OrganizationId, ProductId, "B1", null, null);

        batch.FillMissingDates(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        Assert.Equal(new DateOnly(2026, 9, 1), batch.ManufactureDate);
        Assert.Equal(new DateOnly(2026, 9, 3), batch.ExpiryDate);
    }

    [Fact]
    public void A_later_receipt_may_not_move_a_date_that_is_already_set()
    {
        // The batch is already attached to layers that were costed and reported under it, so a
        // silently moving expiry date reads as a data-entry fix right up until a report disagrees
        // with a printed document.
        var batch = ProductBatch.Create(
            OrganizationId, ProductId, "B1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        batch.FillMissingDates(new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1));

        Assert.Equal(new DateOnly(2026, 9, 1), batch.ManufactureDate);
        Assert.Equal(new DateOnly(2026, 9, 3), batch.ExpiryDate);
    }

    [Fact]
    public void Filling_a_blank_still_cannot_produce_an_impossible_pair()
    {
        var batch = ProductBatch.Create(OrganizationId, ProductId, "B1", new DateOnly(2026, 9, 3), null);

        Assert.Throws<InvalidOperationException>(
            () => batch.FillMissingDates(null, new DateOnly(2026, 9, 1)));
    }
}
