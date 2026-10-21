using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosOrderInvoice;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Pos.Commands.UpdatePosOrderTakeAway;
using ErpApp.Application.Pos.Queries.FindPosSales;
using ErpApp.Application.Pos.Queries.GetPosRefundableSale;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.ListCreditNotes;
using ErpApp.Application.Sales.Queries.SalesMasterReport;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
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

    // ---- Phase 85: a restaurant bill, its service charge and packaging fee ---------------------------

    [Fact]
    public async Task A_restaurant_bill_refunded_at_another_branch_takes_its_fees_out_of_the_selling_branchs_accounts()
    {
        var r = await PosTestRestaurant.CreateAsync([.. PosTestRestaurant.WaiterKeys.Union(PosTestTill.CashierKeys)]);
        var home = r.Till;
        var branch = await home.AddBranchAsync();

        // Each branch keeps its fees in its own accounts; the tenant defaults are a third pair.
        var homeServiceCharge = await AddAccountAsync(home, "4101", "Service Charge - Thamel");
        var homePackaging = await AddAccountAsync(home, "4301", "Packaging - Thamel");
        var branchServiceCharge = await AddAccountAsync(home, "4102", "Service Charge - Lakeside");
        var branchPackaging = await AddAccountAsync(home, "4302", "Packaging - Lakeside");
        await SetFeesAsync(home, serviceCharge: true, homeServiceCharge, TwentyABox, homePackaging);
        await SetFeesAsync(branch, serviceCharge: true, branchServiceCharge, TwentyABox, branchPackaging);

        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();

        // Two identical tables at home, one Momo eaten in and one taken away: 248.60 + 271.20 = 519.80, rounded to 520,
        // with 40 of service charge and 20 of packaging fee.
        var away = await BillWithAParcelAsync(r, r.T1, homeSession.Id);
        var twin = await BillWithAParcelAsync(r, r.T2, homeSession.Id);
        Assert.Equal((40m, 20m, 520m), (away.ServiceCharge, away.PackagingFee, away.GrandTotal));
        Assert.Equal(-80m, await home.BalanceAsync(homeServiceCharge));
        Assert.Equal(-40m, await home.BalanceAsync(homePackaging));

        // The search offers it at the branch, and the branch reads it as refundable.
        var match = Assert.Single(await FindAsync(branch, away.Code));
        Assert.Equal(home.Location.Name, match.SoldAtLocationName);
        Assert.True((await RefundableAsync(branch, away.Id)).CanRefund);

        // The parcel comes back at the branch; the twin's parcel at home.
        var refundedAway = await RefundLineAsync(branch, branchSession.Id, away.Id, parcel: true);
        var refundedHome = await RefundLineAsync(home, homeSession.Id, twin.Id, parcel: true);

        Assert.Equal(refundedHome.GrandTotal, refundedAway.GrandTotal);
        Assert.Equal(refundedHome.RoundOff, refundedAway.RoundOff);
        Assert.Equal(refundedHome.PaidOut, refundedAway.PaidOut);
        Assert.Equal(await PostedAsync(home, refundedHome.Id), await PostedAsync(home, refundedAway.Id));

        // The selling branch's accounts gave both back; the refunding branch's and the tenant's were never touched.
        Assert.Equal(-80m + 40m, await home.BalanceAsync(homeServiceCharge));
        Assert.Equal(-40m + 40m, await home.BalanceAsync(homePackaging));
        foreach (var untouched in new[] { branchServiceCharge, branchPackaging, home.ServiceChargeAccountId, home.PackagingFeeAccountId })
        {
            Assert.Equal(0m, await home.BalanceAsync(untouched));
        }

        // Every entry the away refund posted is the branch's, where the money left (phase 84 Decision D).
        var entries = await home.EntriesForAsync(DocumentType.CreditNote, refundedAway.Id);
        Assert.All(entries, x => Assert.Equal(branch.Location.Id, x.LocationId));

        // The rest of each bill comes back at home: each sale ties out to the paisa either way.
        var lastAway = await RefundLineAsync(home, homeSession.Id, away.Id, parcel: false);
        var lastTwin = await RefundLineAsync(home, homeSession.Id, twin.Id, parcel: false);
        Assert.Equal(lastTwin.GrandTotal, lastAway.GrandTotal);
        Assert.Equal(520m, refundedAway.GrandTotal + lastAway.GrandTotal);
        Assert.Equal(0m, await home.BalanceAsync(homeServiceCharge));
        Assert.Equal(0m, await home.BalanceAsync(homePackaging));
        Assert.Equal(0m, await home.BalanceAsync(await home.ReceivableAccountIdAsync()));
        Assert.True(Assert.Single(await FindAsync(branch, away.Code)).FullyRefunded);

        // Voiding the away refund puts its fees back where the sale had them, under the branch's stamp.
        await branch.VoidRefundAsync(refundedAway.Id);
        Assert.Equal(-20m, await home.BalanceAsync(homeServiceCharge));
        Assert.Equal(-20m, await home.BalanceAsync(homePackaging));
        Assert.Equal(0m, await home.BalanceAsync(branchServiceCharge));
        Assert.All(
            await home.EntriesForAsync(DocumentType.CreditNote, refundedAway.Id),
            x => Assert.Equal(branch.Location.Id, x.LocationId));
    }

    [Fact]
    public async Task A_branch_that_charges_no_fees_and_has_no_accounts_for_them_still_refunds_a_bill_that_carried_them()
    {
        var r = await PosTestRestaurant.CreateAsync([.. PosTestRestaurant.WaiterKeys.Union(PosTestTill.CashierKeys)]);
        var home = r.Till;
        var branch = await home.AddBranchAsync();

        var homeServiceCharge = await AddAccountAsync(home, "4101", "Service Charge - Thamel");
        var homePackaging = await AddAccountAsync(home, "4301", "Packaging - Thamel");
        await SetFeesAsync(home, serviceCharge: true, homeServiceCharge, TwentyABox, homePackaging);
        // The branch is a shop: no service charge, no fee, no account for either here or as a tenant default. The
        // round-off still has the tenant's.
        await SetFeesAsync(branch, serviceCharge: false, null, PackagingFee.None, null);
        var settings = await home.Db.TenantSettings.SingleAsync(x => x.OrganizationId == home.OrganizationId);
        settings.SetPosDefaults(null, home.RoundingAccountId, home.OverShortAccountId, null);
        await home.Db.SaveChangesAsync();

        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var sale = await BillWithAParcelAsync(r, r.T1, homeSession.Id);

        var refund = await RefundLineAsync(branch, branchSession.Id, sale.Id, parcel: true);

        Assert.True(refund.GrandTotal > 0m);
        Assert.Equal(-40m + 20m, await home.BalanceAsync(homeServiceCharge));
        Assert.Equal(-20m + 20m, await home.BalanceAsync(homePackaging));
    }

    // ---- Phase 86: the accounts the sale used, after the setting changed --------------------------------

    [Fact]
    public async Task A_refund_after_the_location_changed_its_fee_accounts_reverses_into_the_accounts_the_sale_used()
    {
        var r = await PosTestRestaurant.CreateAsync([.. PosTestRestaurant.WaiterKeys.Union(PosTestTill.CashierKeys)]);
        var home = r.Till;
        var branch = await home.AddBranchAsync();

        var oldServiceCharge = await AddAccountAsync(home, "4101", "Service Charge - Thamel");
        var oldPackaging = await AddAccountAsync(home, "4301", "Packaging - Thamel");
        var newServiceCharge = await AddAccountAsync(home, "4111", "Service Charge - Thamel 2083");
        var newPackaging = await AddAccountAsync(home, "4311", "Packaging - Thamel 2083");
        await SetFeesAsync(home, serviceCharge: true, oldServiceCharge, TwentyABox, oldPackaging);

        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        var away = await BillWithAParcelAsync(r, r.T1, homeSession.Id);
        var twin = await BillWithAParcelAsync(r, r.T2, homeSession.Id);

        // Each bill keeps the two accounts its approval credited.
        foreach (var bill in new[] { away.Id, twin.Id })
        {
            var sale = await home.Db.Invoices.SingleAsync(x => x.Id == bill);
            Assert.Equal((oldServiceCharge, oldPackaging), (sale.ServiceChargeAccountId, sale.PackagingFeeAccountId));
        }

        // The head office moves its fees to new accounts; a bill after that keeps the new ones.
        await SetFeesAsync(home, serviceCharge: true, newServiceCharge, TwentyABox, newPackaging);
        var later = await BillWithAParcelAsync(r, r.R1, homeSession.Id);
        var laterSale = await home.Db.Invoices.SingleAsync(x => x.Id == later.Id);
        Assert.Equal((newServiceCharge, newPackaging), (laterSale.ServiceChargeAccountId, laterSale.PackagingFeeAccountId));
        Assert.Equal(-40m, await home.BalanceAsync(newServiceCharge));

        // The two earlier parcels come back, one at the branch and one at home: both out of the old accounts, line for line.
        var refundedAway = await RefundLineAsync(branch, branchSession.Id, away.Id, parcel: true);
        var refundedHome = await RefundLineAsync(home, homeSession.Id, twin.Id, parcel: true);
        Assert.Equal(await PostedAsync(home, refundedHome.Id), await PostedAsync(home, refundedAway.Id));

        Assert.Equal(-80m + 40m, await home.BalanceAsync(oldServiceCharge));
        Assert.Equal(-40m + 40m, await home.BalanceAsync(oldPackaging));
        Assert.Equal(-40m, await home.BalanceAsync(newServiceCharge));
        Assert.Equal(-20m, await home.BalanceAsync(newPackaging));

        // The rest of both bills ties the old accounts out to nothing; the later bill is untouched.
        await RefundLineAsync(home, homeSession.Id, away.Id, parcel: false);
        await RefundLineAsync(home, homeSession.Id, twin.Id, parcel: false);
        Assert.Equal(0m, await home.BalanceAsync(oldServiceCharge));
        Assert.Equal(0m, await home.BalanceAsync(oldPackaging));
        Assert.Equal(-40m, await home.BalanceAsync(newServiceCharge));
        Assert.Equal(-20m, await home.BalanceAsync(newPackaging));
    }

    [Fact]
    public async Task A_sale_that_recorded_no_accounts_refunds_through_the_selling_locations_current_setting()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();

        // A counter sale at a location with no service charge account of its own keeps the tenant default it posted to.
        var sale = await home.SellAsync(homeSession.Id, [home.Momo(1)], [home.Cash(249m)]);
        var invoice = await home.Db.Invoices.SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(home.ServiceChargeAccountId, invoice.ServiceChargeAccountId);
        Assert.Null(invoice.PackagingFeeAccountId);

        // As every sale approved before phase 86: nothing recorded. Then the location takes its own account.
        ((DbContext)home.Db).Entry(invoice).Property(nameof(Invoice.ServiceChargeAccountId)).CurrentValue = null;
        await home.Db.SaveChangesAsync();
        var thamel = await AddAccountAsync(home, "4101", "Service Charge - Thamel");
        await SetFeesAsync(home, serviceCharge: true, thamel, PackagingFee.None, null);

        await RefundLineAsync(branch, branchSession.Id, sale.Id, parcel: false);

        // Phase 85's rule: the selling location's account as it is now.
        Assert.Equal(20m, await home.BalanceAsync(thamel));
        Assert.Equal(-20m, await home.BalanceAsync(home.ServiceChargeAccountId));
    }

    // ---- Phase 96: a refund rounds into the account its sale rounded into ---------------------------

    [Fact]
    public async Task A_refund_after_the_location_changed_its_round_off_account_rounds_into_the_account_the_sale_used()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var oldRounding = await AddAccountAsync(home, "4201", "Rounding - Thamel");
        var newRounding = await AddAccountAsync(home, "4211", "Rounding - Thamel 2083");
        var branchRounding = await AddAccountAsync(home, "4221", "Rounding - Lakeside");
        await SetRoundOffAccountAsync(home, oldRounding);
        await SetRoundOffAccountAsync(branch, branchRounding);

        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();

        // Two identical sales: 3 Coke, 203.40 rounded to 203. Each keeps the account its round-off went to.
        var away = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);
        var twin = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);
        foreach (var id in new[] { away.Id, twin.Id })
        {
            Assert.Equal(oldRounding, (await home.Db.Invoices.SingleAsync(x => x.Id == id)).RoundingAccountId);
        }

        Assert.Equal(0.80m, await home.BalanceAsync(oldRounding));

        // Head office moves its round-off; a later sale keeps the new account.
        await SetRoundOffAccountAsync(home, newRounding);
        var later = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);
        Assert.Equal(newRounding, (await home.Db.Invoices.SingleAsync(x => x.Id == later.Id)).RoundingAccountId);

        // Two of the first come back at the branch, two of the twin at home: both round into the old account.
        var refundedAway = await branch.RefundAsync(
            branchSession.Id, away.Id, [await branch.ReturnAsync(away.Id, home.CokeId, 2m)], [branch.Cash(136m)]);
        var refundedHome = await home.RefundAsync(
            homeSession.Id, twin.Id, [await home.ReturnAsync(twin.Id, home.CokeId, 2m)], [home.Cash(136m)]);
        Assert.NotEqual(0m, refundedAway.RoundOff);
        Assert.Equal(await PostedAsync(home, refundedHome.Id), await PostedAsync(home, refundedAway.Id));
        Assert.Equal(0m, await home.BalanceAsync(branchRounding));

        // The last Coke of each: the old account ties out to nothing, as if no setting had moved.
        await home.RefundAsync(
            homeSession.Id, away.Id, [await home.ReturnAsync(away.Id, home.CokeId, 1m)], [home.Cash(67m)]);
        await home.RefundAsync(
            homeSession.Id, twin.Id, [await home.ReturnAsync(twin.Id, home.CokeId, 1m)], [home.Cash(67m)]);
        Assert.Equal(0m, await home.BalanceAsync(oldRounding));
        Assert.Equal(0.40m, await home.BalanceAsync(newRounding));
        Assert.Equal(0m, await home.BalanceAsync(branchRounding));
        Assert.Equal(0m, await home.BalanceAsync(home.RoundingAccountId));
    }

    [Fact]
    public async Task A_sale_that_recorded_no_rounding_account_refunds_through_the_refunding_locations_current_setting()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var branchRounding = await AddAccountAsync(home, "4221", "Rounding - Lakeside");
        await SetRoundOffAccountAsync(branch, branchRounding);
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();

        var sale = await home.SellAsync(homeSession.Id, [home.Coke(3)], [home.Cash(203m)]);
        var invoice = await home.Db.Invoices.SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(home.RoundingAccountId, invoice.RoundingAccountId);

        // As every sale approved before phase 96: nothing recorded, so the note rounds as every note did before.
        ((DbContext)home.Db).Entry(invoice).Property(nameof(Invoice.RoundingAccountId)).CurrentValue = null;
        await home.Db.SaveChangesAsync();

        var refunded = await branch.RefundAsync(
            branchSession.Id, sale.Id, [await branch.ReturnAsync(sale.Id, home.CokeId, 2m)], [branch.Cash(136m)]);

        Assert.Equal(refunded.RoundOff, await home.BalanceAsync(branchRounding));
        Assert.Equal(0.40m, await home.BalanceAsync(home.RoundingAccountId));
    }

    [Fact]
    public async Task A_sale_not_rounded_records_no_rounding_account()
    {
        var home = await PosTestTill.CreateAsync();
        var session = await home.OpenAsync();

        // 5 Coke: 300 + 39 VAT = 339, a whole rupee, so nothing is rounded and no account is kept.
        var whole = await home.SellAsync(session.Id, [home.Coke(5)], [home.Cash(339m)]);

        var invoice = await home.Db.Invoices.SingleAsync(x => x.Id == whole.Id);
        Assert.Equal(0m, invoice.RoundOff);
        Assert.Null(invoice.RoundingAccountId);
    }

    [Fact]
    public async Task The_credit_note_page_names_the_warehouse_a_refunds_goods_went_back_into()
    {
        var home = await PosTestTill.CreateAsync();
        var branch = await home.AddBranchAsync();
        var homeSession = await home.OpenAsync();
        var branchSession = await branch.OpenAsync();
        // 2 Coke 135.60 + a Momo 248.60 = 384.20, rounded to 384.
        var sale = await home.SellAsync(homeSession.Id, [home.Coke(2), home.Momo(1)], [home.Cash(384m)]);

        async Task<CreatePosRefundResult> BackAsync(PosTestTill till, Guid sessionId, Guid productId)
        {
            var line = await till.ReturnAsync(sale.Id, productId, 1m);
            var preview = await till.PreviewRefundAsync(sessionId, sale.Id, [line]);
            return await till.RefundAsync(sessionId, sale.Id, [line], [till.Cash(preview.RequiredPayout)]);
        }

        var away = await BackAsync(branch, branchSession.Id, home.CokeId);
        var atHome = await BackAsync(home, homeSession.Id, home.CokeId);
        // A Momo is a service: nothing goes back into stock, so no warehouse is named.
        var momo = await BackAsync(home, homeSession.Id, home.MomoId);

        async Task<string?> WarehouseOfAsync(Guid id) =>
            (await new GetCreditNoteQueryHandler(home.Db).Handle(
                new GetCreditNoteQuery(home.OrganizationId, id), CancellationToken.None)).ReturnWarehouseName;

        var homeWarehouse = await home.Db.Warehouses.Where(x => x.Id == home.Seed.WarehouseId).Select(x => x.Name).SingleAsync();
        Assert.Equal("Lakeside Branch Store", await WarehouseOfAsync(away.Id));
        Assert.Equal(homeWarehouse, await WarehouseOfAsync(atHome.Id));
        Assert.Null(await WarehouseOfAsync(momo.Id));

        // A refund from before phase 84 names no warehouse of its own; the page reads the sale's, where it went.
        var note = await home.Db.CreditNotes.SingleAsync(x => x.Id == atHome.Id);
        ((DbContext)home.Db).Entry(note).Property(nameof(CreditNote.WarehouseId)).CurrentValue = null;
        await home.Db.SaveChangesAsync();
        Assert.Equal(homeWarehouse, await WarehouseOfAsync(atHome.Id));

        // Phase 86 -- the list names the same warehouse on each card, through the same reader.
        async Task<Dictionary<Guid, string?>> ListedAsync() =>
            (await new ListCreditNotesQueryHandler(home.Db, home.CurrentUser()).Handle(
                new ListCreditNotesQuery(home.OrganizationId, null), CancellationToken.None))
            .Items.ToDictionary(x => x.Id, x => x.ReturnWarehouseName);

        var listed = await ListedAsync();
        Assert.Equal("Lakeside Branch Store", listed[away.Id]);
        Assert.Equal(homeWarehouse, listed[atHome.Id]);
        Assert.Null(listed[momo.Id]);

        // A voided refund has taken its goods back out: neither the card nor the page names a warehouse any more.
        await branch.VoidRefundAsync(away.Id);
        Assert.Null((await ListedAsync())[away.Id]);
        Assert.Null(await WarehouseOfAsync(away.Id));
    }

    // ---- What may not come back elsewhere ---------------------------------------------------------

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

    private static readonly PackagingFee TwentyABox = PackagingFee.Create(PackagingFeeBasis.PerUnit, 20m);

    private static async Task<Guid> AddAccountAsync(PosTestTill till, string code, string name)
    {
        var account = Account.Create(till.OrganizationId, code, name, AccountRootType.Income, Guid.NewGuid());
        till.Db.Accounts.Add(account);
        await till.Db.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>One location's service charge (10%, on take-away too) and packaging fee, each with its own account.</summary>
    private static async Task SetFeesAsync(
        PosTestTill till, bool serviceCharge, Guid? serviceChargeAccountId, PackagingFee fee, Guid? packagingFeeAccountId)
    {
        var s = await till.Db.PosLocationSettings.SingleAsync(x => x.BillingLocationId == till.Location.Id);
        s.Update(
            till.Location.PosMode, serviceCharge, s.ServiceChargeRate, serviceChargeAccountId, s.ServiceChargeOnTakeAway,
            fee, packagingFeeAccountId, s.RoundOffEnabled, s.RoundOffAccountId, s.CashVerificationRequired, s.Denominations,
            s.DefaultTab, s.PrintEstimateBill, s.PrintInvoice, s.PrintCreditNote, s.PrintKot, s.AbbreviatedTaxInvoiceEnabled);
        await till.Db.SaveChangesAsync();
    }

    /// <summary>Phase 96 -- one location's Round Off account, everything else as it is.</summary>
    private static async Task SetRoundOffAccountAsync(PosTestTill till, Guid? roundOffAccountId)
    {
        var s = await till.Db.PosLocationSettings.SingleAsync(x => x.BillingLocationId == till.Location.Id);
        s.Update(
            till.Location.PosMode, s.ServiceChargeEnabled, s.ServiceChargeRate, s.ServiceChargeAccountId,
            s.ServiceChargeOnTakeAway, s.PackagingFee, s.PackagingFeeAccountId, s.RoundOffEnabled, roundOffAccountId,
            s.CashVerificationRequired, s.Denominations, s.DefaultTab, s.PrintEstimateBill, s.PrintInvoice, s.PrintCreditNote,
            s.PrintKot, s.AbbreviatedTaxInvoiceEnabled);
        await till.Db.SaveChangesAsync();
    }

    /// <summary>Two Momo at a table, one of them marked as a parcel, billed whole and paid in cash.</summary>
    private static async Task<CreatePosOrderInvoiceResult> BillWithAParcelAsync(PosTestRestaurant r, Guid table, Guid sessionId)
    {
        var order = await r.SeatAsync(table, [r.Momo(2)]);
        await new UpdatePosOrderTakeAwayCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderTakeAwayCommand(r.OrganizationId, order.Id, order.Lines.Single().Id, 1m),
            CancellationToken.None);
        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        return await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Cash(preview.Total)]);
    }

    /// <summary>Refunds the sale's parcel line (the one carrying a packaging fee) or its other line, paid out in cash
    /// at whatever the till's own preview says is owed back.</summary>
    private static async Task<CreatePosRefundResult> RefundLineAsync(PosTestTill till, Guid sessionId, Guid invoiceId, bool parcel)
    {
        var line = await till.Db.InvoiceLines
            .Where(x => x.InvoiceId == invoiceId && (x.PackagingFeeAmount > 0m) == parcel)
            .SingleAsync();
        var input = new PosRefundLineInput(line.Id, line.Quantity);
        var preview = await till.PreviewRefundAsync(sessionId, invoiceId, [input]);
        return await till.RefundAsync(sessionId, invoiceId, [input], [till.Cash(preview.RequiredPayout)]);
    }

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
