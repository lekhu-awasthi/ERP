using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 37 -- Negative Item Balance made real.
///
/// <para><b>These tests replace phase 26c's
/// <c>Stock_cannot_go_negative_yet_which_is_why_the_readers_negative_balance_guard_is_unreachable</c>,
/// deliberately.</b> That test pinned a fact, not a requirement: <c>ConsumeAsync</c> threw on any
/// oversell, so <c>StockFactReader</c>'s zero-value branch could never run and had to be defended
/// from tidy-minded deletion. What changed is the fact, not the intent -- the tenant setting the
/// reference product has always had (Reject / Warn / Do Nothing) now decides, so the throw is one
/// of three behaviours rather than the only one. What replaces the pin is stronger than it: Reject
/// still throws (the same assertion, now conditional on the setting), the other two reach the
/// guard, and the guard's own output is asserted rather than merely protected.</para>
/// </summary>
public sealed class NegativeStockTests
{
    private static readonly DateOnly PeriodStart = new(2026, 1, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 12, 31);

    /// <summary>
    /// The replacement for phase-26c's pin, first half: a Reject tenant behaves exactly as every
    /// tenant used to, and the 409 is raised by the availability gate rather than from inside the
    /// FIFO engine, so the message names the document rather than the layers.
    /// </summary>
    [Fact]
    public async Task A_Reject_tenant_still_cannot_oversell()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await SetNegativeStockActionAsync(db, seed, BalanceAction.Reject);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);

        await Assert.ThrowsAsync<ConflictException>(() =>
            InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m));

        Assert.Empty(await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));
    }

    /// <summary>
    /// The replacement's second half: a Warn tenant that confirms, and a Do Nothing tenant that is
    /// never asked, both end up with the same thing -- a shortfall layer -- because the setting
    /// decides whether the user is interrupted, not what the ledger records.
    /// </summary>
    [Theory]
    [InlineData(BalanceAction.Warn)]
    [InlineData(BalanceAction.DoNothing)]
    public async Task Warn_and_Do_Nothing_leave_a_shortfall_layer_at_the_last_known_cost(BalanceAction action)
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await SetNegativeStockActionAsync(db, seed, action);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m);

        var shortfall = Assert.Single(
            await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));

        Assert.Equal(-3m, shortfall.QuantityRemaining);
        Assert.Equal(-3m, shortfall.QuantityIn);
        Assert.Equal(10m, shortfall.UnitCost); // the only cost this product has ever had here
        Assert.Equal(-30m, await OnHandValueAsync(db, seed));
    }

    /// <summary>
    /// A Warn tenant is still interrupted once. This is the difference the setting is actually for:
    /// the first attempt is refused with a confirmable warning, and the second -- the same command
    /// with the override flag -- goes through. Before this phase the second attempt hit
    /// <c>ConsumeAsync</c>'s own throw and Warn was indistinguishable from Reject.
    /// </summary>
    [Fact]
    public async Task A_Warn_tenant_must_confirm_before_the_shortfall_is_recorded()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);

        var invoiceId = await InventoryReportSeed.DraftInvoiceAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m);

        await Assert.ThrowsAsync<StockAvailabilityWarningException>(() =>
            InventoryReportSeed.ApproveInvoiceAsync(db, seed, invoiceId, overrideWarning: false));
        Assert.Empty(await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));

        await InventoryReportSeed.ApproveInvoiceAsync(db, seed, invoiceId, overrideWarning: true);
        Assert.Single(await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));
    }

    /// <summary>
    /// <b>The phase's acceptance test.</b> Sell into a shortfall at one assumed cost, cover it with
    /// a purchase at another, and all three views of stock value still agree -- the FIFO layers,
    /// the Inventory account and the movement history. The gap between the assumed and the real
    /// cost is the catch-up, and it is the number that has to reach all three or two of them drift
    /// apart quietly and for ever.
    /// </summary>
    [Fact]
    public async Task Filling_a_shortfall_at_a_different_cost_keeps_the_ledger_the_GL_and_the_movements_in_step()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);

        // Eight sold out of five on hand: five layers at 10.00 and three units owed at the only
        // cost the product has ever had, also 10.00.
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m);
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
        Assert.Equal(-30m, await OnHandValueAsync(db, seed));

        // The bill turns up at 14.00, not the 10.00 the shortfall was issued at. Ten arrive, three
        // of them go straight back out to cover the debt, and the catch-up is 3 x (14 - 10) = 12.
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 10m, 14m);
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);

        Assert.Empty(await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));
        Assert.Equal(7m * 14m, await OnHandValueAsync(db, seed));

        var adjustment = Assert.Single(
            await db.StockMovements.Where(x => x.ValueAdjustment != 0).ToListAsync(CancellationToken.None));
        Assert.Equal(12m, adjustment.ValueAdjustment);
        Assert.Equal(StockMovementDirection.Out, adjustment.Direction);
        Assert.Equal(0m, adjustment.Quantity);
    }

    /// <summary>
    /// The catch-up is posted, not absorbed: a second GL entry against the covering bill, Debit
    /// Inventory Adjustment / Credit Inventory. It shares the bill's (SourceDocumentType,
    /// SourceDocumentId) pair, which is exactly why phase 36's reader had to replace every
    /// <c>SingleAsync</c> over that pair first.
    /// </summary>
    [Fact]
    public async Task The_catch_up_posts_its_own_entry_against_the_covering_document()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m);
        var bill = await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 10m, 14m);

        var entries = await db.GlJournalEntries
            .Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == ErpApp.Domain.Common.DocumentType.PurchaseBill
                && x.SourceDocumentId == bill.Id)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal(entry.Lines.Sum(x => x.Debit), entry.Lines.Sum(x => x.Credit));
        }

        var adjustmentAccountId = await db.TenantSettings
            .Where(x => x.OrganizationId == seed.OrganizationId)
            .Select(x => x.DefaultInventoryAdjustmentAccountId)
            .SingleAsync(CancellationToken.None);

        var catchUp = Assert.Single(entries.Where(e => e.Lines.Any(l => l.AccountId == adjustmentAccountId)));
        Assert.Equal(12m, catchUp.Lines.Single(l => l.AccountId == adjustmentAccountId).Debit);
    }

    /// <summary>
    /// Phase 26c wrote the zero-value branch two phases before anything could reach it, on the
    /// grounds that a report must already be right about negative stock on the day the setting
    /// ships. This is that day: a negative balance reports no value at all, because there is no
    /// cost to carry for goods that are not there -- which is what the live report prints.
    /// </summary>
    [Fact]
    public async Task A_negative_balance_reports_no_value_which_is_the_branch_phase_26c_was_defending()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m);

        var result = await new InventoryPositionReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new InventoryPositionReportQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null, null),
            CancellationToken.None);

        var row = result.Items.Single(r => r.ProductId == seed.ProductId);

        Assert.Equal(-3m, row.Quantity);
        Assert.Equal(0m, row.Amount);
        Assert.Equal(0m, row.Rate);
    }

    /// <summary>
    /// A shortfall is paid off oldest first, and a receipt that only partly covers it leaves the
    /// rest owed -- with the catch-up charged on what was actually filled, not on the whole debt.
    /// </summary>
    [Fact]
    public async Task A_partial_receipt_fills_what_it_can_and_leaves_the_rest_owed()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);
        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 11m, 20m);
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 4m, 13m);

        var shortfall = Assert.Single(
            await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));
        // Six were owed; four arrived, so two are still owed and QuantityIn keeps the original debt
        // -- the same "QuantityIn never changes" rule a consumed layer follows, for the same
        // kardex-reconstruction reason.
        Assert.Equal(-6m, shortfall.QuantityIn);
        Assert.Equal(-2m, shortfall.QuantityRemaining);

        var adjustment = Assert.Single(
            await db.StockMovements.Where(x => x.ValueAdjustment != 0).ToListAsync(CancellationToken.None));
        Assert.Equal(4m * (13m - 10m), adjustment.ValueAdjustment);

        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    /// <summary>
    /// A product that has never been received into this warehouse has no price to guess from, so
    /// its shortfall is carried at zero rather than at an invented figure -- and the whole of its
    /// real cost lands as the catch-up when the first receipt arrives. The conservation law is what
    /// makes that safe: nothing is lost, it is only recognised later.
    /// </summary>
    [Fact]
    public async Task A_product_never_bought_here_carries_its_shortfall_at_zero_until_the_first_receipt()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 4m, 20m);

        var shortfall = Assert.Single(
            await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));
        Assert.Equal(0m, shortfall.UnitCost);
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(3), 4m, 9m);

        var adjustment = Assert.Single(
            await db.StockMovements.Where(x => x.ValueAdjustment != 0).ToListAsync(CancellationToken.None));
        Assert.Equal(36m, adjustment.ValueAdjustment);
        Assert.Equal(0m, await OnHandValueAsync(db, seed));
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    /// <summary>
    /// Voiding the invoice that overslept the warehouse puts the stock back and closes its own
    /// shortfall, leaving nothing owed and the three views level again. The void's restock cost is
    /// the very blend the shortfall was issued at, so it catches up nothing -- which is the case
    /// the handler's comment claims and is worth pinning, because getting it wrong would show up as
    /// a phantom adjustment on every voided oversell.
    /// </summary>
    [Fact]
    public async Task Voiding_the_oversold_invoice_closes_its_own_shortfall_without_a_catch_up()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 5m, 10m);
        var invoice = await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 8m, 20m);

        await InventoryReportSeed.VoidInvoiceAsync(db, seed, invoice.Id);

        Assert.Empty(await db.StockLedgerEntries.Where(x => x.QuantityRemaining < 0).ToListAsync(CancellationToken.None));
        Assert.Empty(await db.StockMovements.Where(x => x.ValueAdjustment != 0).ToListAsync(CancellationToken.None));
        Assert.Equal(50m, await OnHandValueAsync(db, seed));
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    /// <summary>
    /// <b>Why the sales return needed no equivalent of the purchase return's fix.</b> The question
    /// phase 37 owed an answer to either way. A Credit Note <i>adds</i> stock, and it adds it at the
    /// cost its source invoice recorded when that stock left -- a figure stored on the invoice line,
    /// not one FIFO picks. So the value it puts into the ledger and the value it credits COGS for
    /// are the same number by construction. The Debit Note's problem was the mirror of that and is
    /// specific to relieving: FIFO hands back whatever the oldest layer costs, which need not be
    /// anything to do with the document being returned against.
    ///
    /// <para>Asserted over a two-price ledger -- the invoice consumes a cheap layer and a dear one
    /// and returns some of both -- because a single-cost ledger would pass whatever the rule was.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_sales_return_already_agreed_because_it_restocks_at_the_cost_it_recorded()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 4m, 10m);
        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(2), 6m, 25m);

        // Six sold: four out of the 10.00 layer and two out of the 25.00 one, a blend of 15.00.
        var invoice = await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(3), 6m, 40m);
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);

        await InventoryReportSeed.CreditNoteAsync(db, seed, PeriodStart.AddDays(4), 2m, 40m, invoice.Id);

        Assert.Equal(4m * 25m + 2m * 15m, await OnHandValueAsync(db, seed));
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    private static async Task<decimal> OnHandValueAsync(IAppDbContext db, InventoryReportSeed.Seed seed)
    {
        var rows = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId)
            .Select(x => new { x.QuantityRemaining, x.UnitCost })
            .ToListAsync(CancellationToken.None);

        return rows.Sum(x => x.QuantityRemaining * x.UnitCost);
    }

    private static async Task SetNegativeStockActionAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, BalanceAction action)
    {
        var settings = await db.TenantSettings.SingleAsync(
            x => x.OrganizationId == seed.OrganizationId, CancellationToken.None);

        settings.UpdateSettings(
            settings.SuggestSellingPriceMode, settings.ProductPriceBasis, settings.InventoryTrackingMode,
            settings.NegativeCashBalanceAction, action, settings.CreditLimitExceedsAction);

        await db.SaveChangesAsync(CancellationToken.None);
    }
}
