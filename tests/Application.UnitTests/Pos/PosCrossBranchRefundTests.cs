using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.FindPosSales;
using ErpApp.Application.Pos.Queries.GetPosRefundableSale;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Sales.Queries.SalesMasterReport;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 84 -- a till sale refunded at another branch of the same organization. The vendor has no such control (its
/// till's refund list is pinned to the till's own location, read live 2026-10-11), so ours is a decision: a retail
/// sale can come back at any branch, under the refund's own keys there, and its goods go back into stock where they
/// now are. The way round it replaces is the customer travelling back to the branch that sold it, so that is the
/// twin every figure is held to.
/// </summary>
public sealed class PosCrossBranchRefundTests
{
    private static DateOnly Today => PosTestTill.Today;

    // ---- The twin: a refund at another branch leaves exactly what a refund at home leaves ------

    [Fact]
    public async Task A_retail_sale_refunded_at_another_branch_leaves_what_the_same_refund_at_home_leaves()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();

        // Two identical sales at home: 3 Coke, 180 + 23.40 VAT = 203.40, rounded to 203.
        var away = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);
        var twin = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);

        // Two of the first come back at the branch; two of the twin at home.
        var refundedAway = await branch.RefundAsync(
            branchSession.Id, away.Id, [await branch.ReturnAsync(away.Id, home.CokeId, 2m)], [branch.Cash(136m)]);
        var refundedHome = await home.RefundAsync(
            homeSession.Id, twin.Id, [await home.ReturnAsync(twin.Id, home.CokeId, 2m)], [home.Cash(136m)]);

        Assert.Equal(refundedHome.GrandTotal, refundedAway.GrandTotal);
        Assert.Equal(refundedHome.RoundOff, refundedAway.RoundOff);
        Assert.Equal(refundedHome.PaidOut, refundedAway.PaidOut);
        Assert.Equal(await PostedAsync(home, refundedHome.Id), await PostedAsync(home, refundedAway.Id));

        // The last Coke of each comes back at home: the sale ties out to the paisa either way.
        var lastAway = await home.RefundAsync(
            homeSession.Id, away.Id, [await home.ReturnAsync(away.Id, home.CokeId, 1m)], [home.Cash(67m)]);
        var lastTwin = await home.RefundAsync(
            homeSession.Id, twin.Id, [await home.ReturnAsync(twin.Id, home.CokeId, 1m)], [home.Cash(67m)]);
        Assert.Equal(lastTwin.GrandTotal, lastAway.GrandTotal);
        Assert.Equal(203m, refundedAway.GrandTotal + lastAway.GrandTotal);
        Assert.Equal(0m, await home.BalanceAsync(await home.ReceivableAccountIdAsync()));
        Assert.Equal(0m, await home.BalanceAsync(home.RoundingAccountId));

        // What differs is where: the note is the branch's, in the branch's session, and its goods are in the
        // branch's warehouse at the cost they left at (40).
        var note = await home.Db.CreditNotes.SingleAsync(x => x.Id == refundedAway.Id);
        Assert.Equal(branch.Location.Id, note.LocationId);
        Assert.Equal(branchSession.Id, note.PosSessionId);
        Assert.Equal(branch.Location.WarehouseId, note.WarehouseId);
        Assert.Equal(away.Code, note.Reference);

        var homeNote = await home.Db.CreditNotes.SingleAsync(x => x.Id == refundedHome.Id);
        Assert.Equal(home.Seed.WarehouseId, homeNote.WarehouseId);

        var atBranch = await home.Db.StockLedgerEntries
            .Where(x => x.OrganizationId == home.OrganizationId && x.WarehouseId == branch.Location.WarehouseId)
            .ToListAsync();
        var layer = Assert.Single(atBranch);
        Assert.Equal(2m, layer.QuantityRemaining);
        Assert.Equal(40m, layer.UnitCost);

        var movements = await home.Db.StockMovements
            .Where(x => x.SourceDocumentType == DocumentType.CreditNote && x.SourceDocumentId == refundedAway.Id)
            .ToListAsync();
        Assert.All(movements, x => Assert.Equal(branch.Location.WarehouseId, x.WarehouseId));
        Assert.All(movements, x => Assert.Equal(branch.Location.Id, x.LocationId));

        await StockConservation.AssertHoldsAsync(home.Db, home.OrganizationId);
    }

    [Fact]
    public async Task The_branch_drawer_pays_it_out_and_the_branch_session_counts_it()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(2)], [home.Cash(136m)]);

        await branch.RefundAsync(
            branchSession.Id, sale.Id, [await branch.ReturnAsync(sale.Id, home.CokeId, 1m)], [branch.Cash(68m)]);

        var branchView = await new GetPosSessionQueryHandler(branch.Db, branch.CurrentUser())
            .Handle(new GetPosSessionQuery(branch.OrganizationId, branchSession.Id), CancellationToken.None);
        Assert.Equal(1000m - 68m, branchView.ExpectedCash);
        Assert.Equal(1, branchView.Sales.Refunds.RefundsCount);

        var homeView = await new GetPosSessionQueryHandler(home.Db, home.CurrentUser())
            .Handle(new GetPosSessionQuery(home.OrganizationId, homeSession.Id), CancellationToken.None);
        Assert.Equal(1000m + 136m, homeView.ExpectedCash);
        Assert.Equal(0, homeView.Sales.Refunds.RefundsCount);
    }

    [Fact]
    public async Task A_split_payout_at_another_branch_takes_each_mode_and_still_sums_to_what_is_owed_back()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);

        var refund = await branch.RefundAsync(
            branchSession.Id, sale.Id, [await branch.ReturnAsync(sale.Id, home.CokeId, 2m)],
            [branch.Cash(100m), branch.Card(36m)]);

        Assert.Equal(136m, refund.PaidOut);
        var note = await home.Db.CreditNotes.Include(x => x.Payouts).SingleAsync(x => x.Id == refund.Id);
        Assert.Equal(2, note.Payouts.Count);
        Assert.Equal(-36m, await home.BalanceAsync(home.BankAccountId));
    }

    // ---- What may not come back elsewhere ---------------------------------------------------------

    [Fact]
    public async Task A_bill_carrying_a_service_charge_is_refunded_only_where_it_was_sold()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        // Chicken Momo carries the location's 10% service charge.
        var sale = await home.SellAsync(homeSession.Id, [home.Momo(1)], [home.Cash(249m)]);
        var line = await branch.ReturnAsync(sale.Id, home.MomoId, 1m);

        var preview = await Assert.ThrowsAsync<ConflictException>(() => branch.PreviewRefundAsync(branchSession.Id, sale.Id, [line]));
        Assert.Contains("service charge", preview.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ConflictException>(() => branch.RefundAsync(branchSession.Id, sale.Id, [line], [branch.Cash(249m)]));
        await Assert.ThrowsAsync<ConflictException>(() => RefundableAsync(branch, sale.Id));

        // The search still names it, and says it is not refundable here.
        var match = Assert.Single(await FindAsync(branch, sale.Code));
        Assert.False(match.RefundableHere);
        Assert.Equal(home.Location.Name, match.SoldAtLocationName);

        // At home it refunds as ever.
        await home.RefundAsync(homeSession.Id, sale.Id, [line], [home.Cash(249m)]);
    }

    [Fact]
    public async Task A_branch_with_no_default_warehouse_cannot_take_back_another_branchs_goods()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync(withWarehouse: false);
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(1)], [home.Cash(68m)]);

        var line = await branch.ReturnAsync(sale.Id, home.CokeId, 1m);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => branch.RefundAsync(
            branchSession.Id, sale.Id, [line], [branch.Cash(68m)]));
        Assert.Contains(branch.Location.Name, refused.Message, StringComparison.Ordinal);
        Assert.Contains("default warehouse", refused.Message, StringComparison.Ordinal);
    }

    // ---- Finding it: by its full number, under the refund's keys at this branch ------------------

    [Fact]
    public async Task Another_branchs_sale_is_found_only_by_its_full_number()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(1)], [home.Cash(68m)]);

        Assert.Empty(await FindAsync(branch, sale.Code[..^1]));
        Assert.Empty(await FindAsync(branch, null));

        var match = Assert.Single(await FindAsync(branch, sale.Code));
        Assert.Equal(sale.Id, match.InvoiceId);
        Assert.Equal(home.Location.Name, match.SoldAtLocationName);
        Assert.True(match.RefundableHere);
        Assert.False(match.FullyRefunded);

        // At home it is an ordinary match: no location named.
        var atHome = Assert.Single(await FindAsync(home, sale.Code));
        Assert.Null(atHome.SoldAtLocationName);

        var refundable = await RefundableAsync(branch, sale.Id);
        Assert.Equal(home.Location.Name, refundable.SoldAtLocationName);
        Assert.Equal("Lakeside Branch Store", refundable.ReturnWarehouseName);
        Assert.True(refundable.CanRefund);
    }

    [Fact]
    public async Task A_cashier_without_approve_at_this_branch_finds_no_other_branchs_sale_and_reads_none()
    {
        // Every till key everywhere except Credit Note Approve, which is granted at home only.
        var keys = PosTestTill.CashierKeys.Where(x => x != PermissionKeys.CreditNoteApprove).ToArray();
        var home = await PosTestTill.CreateAsync(keys);
        home.Db.RolePermissions.Add(
            RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.CreditNoteApprove, true, home.Location.Id));
        await home.Db.SaveChangesAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(1)], [home.Cash(68m)]);

        Assert.Empty(await FindAsync(branch, sale.Code));

        var read = await Assert.ThrowsAsync<ForbiddenException>(() => RefundableAsync(branch, sale.Id));
        Assert.Contains(PermissionKeys.CreditNoteApprove, read.Message, StringComparison.Ordinal);

        var refund = await Assert.ThrowsAsync<ForbiddenException>(async () => await branch.RefundAsync(
            branchSession.Id, sale.Id, [await branch.ReturnAsync(sale.Id, home.CokeId, 1m)], [branch.Cash(68m)]));
        Assert.Contains(PermissionKeys.CreditNoteApprove, refund.Message, StringComparison.Ordinal);

        // At home the same cashier refunds it.
        Assert.True((await RefundableAsync(home, sale.Id)).CanRefund);
    }

    [Fact]
    public async Task A_sale_with_nothing_left_to_give_back_is_marked_fully_refunded()
    {
        var home = await PosTestTill.CreateAsync();
        var session = await home.OpenAsync();
        var sale = await home.SellAsync(session.Id, [home.Coke(2)], [home.Cash(136m)]);

        await home.RefundAsync(session.Id, sale.Id, [await home.ReturnAsync(sale.Id, home.CokeId, 1m)], [home.Cash(68m)]);
        Assert.False(Assert.Single(await FindAsync(home, sale.Code)).FullyRefunded);

        var last = await home.RefundAsync(
            session.Id, sale.Id, [await home.ReturnAsync(sale.Id, home.CokeId, 1m)], [home.Cash(68m)]);
        Assert.True(Assert.Single(await FindAsync(home, sale.Code)).FullyRefunded);

        // A voided refund gives its quantity back, and the mark with it.
        await home.VoidRefundAsync(last.Id);
        Assert.False(Assert.Single(await FindAsync(home, sale.Code)).FullyRefunded);
    }

    // ---- Undoing it, and reporting it ------------------------------------------------------------

    [Fact]
    public async Task Voiding_a_refund_made_at_another_branch_takes_the_goods_back_out_of_that_branchs_warehouse()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(2)], [home.Cash(136m)]);
        var refund = await branch.RefundAsync(
            branchSession.Id, sale.Id, [await branch.ReturnAsync(sale.Id, home.CokeId, 2m)], [branch.Cash(136m)]);

        await branch.VoidRefundAsync(refund.Id);

        var atBranch = await home.Db.StockLedgerEntries
            .Where(x => x.OrganizationId == home.OrganizationId && x.WarehouseId == branch.Location.WarehouseId)
            .SumAsync(x => x.QuantityRemaining);
        Assert.Equal(0m, atBranch);
        Assert.Equal(0m, await home.BalanceAsync(await home.ReceivableAccountIdAsync()));
        await StockConservation.AssertHoldsAsync(home.Db, home.OrganizationId);
    }

    [Fact]
    public async Task Sales_master_reports_the_refund_at_the_warehouse_its_goods_went_back_to()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(2)], [home.Cash(136m)]);
        await branch.RefundAsync(
            branchSession.Id, sale.Id, [await branch.ReturnAsync(sale.Id, home.CokeId, 1m)], [branch.Cash(68m)]);

        var handler = new SalesMasterReportQueryHandler(home.Db, home.CurrentUser());
        var all = await handler.Handle(
            new SalesMasterReportQuery(home.OrganizationId, Today, Today, null, null, null), CancellationToken.None);
        var returned = Assert.Single(all.Rows, x => x.Type == DocumentType.CreditNote);
        Assert.Equal(branch.Location.WarehouseId, returned.WarehouseId);
        Assert.Equal("Lakeside Branch Store", returned.WarehouseName);

        var atBranch = await handler.Handle(
            new SalesMasterReportQuery(home.OrganizationId, Today, Today, null, null, branch.Location.WarehouseId),
            CancellationToken.None);
        Assert.Equal(DocumentType.CreditNote, Assert.Single(atBranch.Rows).Type);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static Task<IReadOnlyList<PosSaleMatchDto>> FindAsync(PosTestTill till, string? search) =>
        new FindPosSalesQueryHandler(till.Db, till.CurrentUser()).Handle(
            new FindPosSalesQuery(till.OrganizationId, till.Location.Id, search), CancellationToken.None);

    private static Task<PosRefundableSaleDto> RefundableAsync(PosTestTill till, Guid invoiceId) =>
        new GetPosRefundableSaleQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosRefundableSaleQuery(till.OrganizationId, till.Location.Id, invoiceId), CancellationToken.None);

    /// <summary>Every line a refund posted, by account, entry order kept: the figures the twin is held to, with the
    /// location stamp (which is meant to differ) left out.</summary>
    private static async Task<List<(Guid Account, decimal Debit, decimal Credit)>> PostedAsync(PosTestTill till, Guid creditNoteId) =>
        [.. (await till.EntriesForAsync(DocumentType.CreditNote, creditNoteId))
            .OrderBy(x => x.Lines.Count)
            .SelectMany(e => e.Lines.OrderBy(l => l.AccountId).ThenBy(l => l.Debit).Select(l => (l.AccountId, l.Debit, l.Credit)))];
}
