using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Queries.GetPosDaySummary;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Sales.Queries.GetInvoice;
using ErpApp.Application.Common.Security;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 61 -- the sale engine, driven through the real handlers on phase 59's written service. Each
/// test ends where the money is: the general ledger, the receivable readers, or the drawer.
/// </summary>
public sealed class PosSaleEngineTests
{
    // ---- The sale and its two entries -------------------------------------------------------

    [Fact]
    public async Task A_till_sale_posts_a_sale_entry_and_a_tender_entry_each_balanced()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        // Phase 59's whole table: 2 momo (service charge) + 2 coke, 632.80 rounded to 633, paid with
        // a 1000 note and 367 in change.
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        Assert.Equal(633m, sale.GrandTotal);
        Assert.Equal(40m, sale.ServiceCharge);
        Assert.Equal(0.20m, sale.RoundOff);
        Assert.Equal(0m, sale.CreditAmount);

        var entries = await till.EntriesForAsync(DocumentType.Invoice, sale.Id);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(e.Lines.Sum(x => x.Debit), e.Lines.Sum(x => x.Credit)));

        var receivable = await till.ReceivableAccountIdAsync();
        Assert.Equal(0m, await till.BalanceAsync(receivable));
        Assert.Equal(633m, await till.BalanceAsync(till.CashAccountId));
        Assert.Equal(-40m, await till.BalanceAsync(till.ServiceChargeAccountId));
        Assert.Equal(-0.20m, await till.BalanceAsync(till.RoundingAccountId));

        var vat = (await till.Db.TenantSettings.SingleAsync(x => x.OrganizationId == till.OrganizationId))
            .DefaultVatPayableAccountId!.Value;
        // VAT on the service charge too: 13% of (400 + 40) + 13% of 120.
        Assert.Equal(-72.80m, await till.BalanceAsync(vat));

        var invoice = await till.Db.Invoices.Include(x => x.Tenders).SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(SalesChannel.Pos, invoice.Channel);
        Assert.Equal(session.Id, invoice.PosSessionId);
        Assert.Equal(PosTab.Retail, invoice.OrderType);
        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Equal(till.Location.Id, invoice.LocationId);
        Assert.Equal(till.WalkInId, invoice.ContactId);
        Assert.Single(invoice.Tenders);

        await StockConservation.AssertHoldsAsync(till.Db, till.OrganizationId);
    }

    [Fact]
    public async Task Rounding_is_to_the_nearest_rupee_so_a_bill_can_be_rounded_down()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        // 200 + 20 + 28.60 + 60 + 7.80 = 316.40. The vendor charged 317 for exactly this bill (it
        // rounds up); Decision B rounds to the nearest rupee, so this one goes down.
        var sale = await till.SellAsync(session.Id, [till.Momo(1), till.Coke(1)], [till.Cash(500m)], change: 184m);

        Assert.Equal(316m, sale.GrandTotal);
        Assert.Equal(-0.40m, sale.RoundOff);
        Assert.Equal(0.40m, await till.BalanceAsync(till.RoundingAccountId));
    }

    [Fact]
    public async Task A_location_with_no_service_charge_charges_none_and_a_product_without_the_flag_never_does()
    {
        var till = await PosTestTill.CreateAsync();
        var settings = await till.Db.PosLocationSettings.SingleAsync();
        settings.Update(
            PosMode.Retail, false, 0m, null, false, null, false, PosLocationSettings.DefaultDenominations, null,
            true, true, true, false);
        await till.Db.SaveChangesAsync();
        var session = await till.OpenAsync();

        var sale = await till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(226m)]);

        Assert.Equal(0m, sale.ServiceCharge);
        Assert.Equal(0m, sale.RoundOff);
        Assert.Equal(226m, sale.GrandTotal);
    }

    [Fact]
    public async Task The_service_charge_account_falls_back_from_the_location_to_the_tenant_and_is_refused_naming_it_when_neither_has_one()
    {
        var till = await PosTestTill.CreateAsync();
        var settings = await till.Db.TenantSettings.SingleAsync(x => x.OrganizationId == till.OrganizationId);
        settings.SetPosDefaults(null, till.RoundingAccountId, till.OverShortAccountId);
        await till.Db.SaveChangesAsync();
        var session = await till.OpenAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)]));
        Assert.Contains("Service Charge", refused.Message, StringComparison.Ordinal);

        // The location's own account, once set, is used without any tenant default.
        var posSettings = await till.Db.PosLocationSettings.SingleAsync();
        posSettings.Update(
            PosMode.Retail, true, 10m, till.VegetablesAccountId, true, null, false,
            PosLocationSettings.DefaultDenominations, null, true, true, true, false);
        await till.Db.SaveChangesAsync();

        await till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)]);
        Assert.Equal(-20m, await till.BalanceAsync(till.VegetablesAccountId));
    }

    // ---- Tenders, change and credit -------------------------------------------------------

    [Fact]
    public async Task Change_comes_only_out_of_cash_and_only_on_a_bill_paid_in_full()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        // A card swiped over the bill cannot give cash back.
        var overCard = await Assert.ThrowsAsync<ValidationException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Card(300m)], change: 51m));
        Assert.Contains("cash handed over", overCard.Message, StringComparison.Ordinal);

        // Tenders less change beyond the bill.
        await Assert.ThrowsAsync<ValidationException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(300m)], change: 10m));

        // Change while leaving part of the bill on credit.
        await Assert.ThrowsAsync<ValidationException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(100m)], change: 10m, contactId: till.Seed.CustomerId));

        Assert.Empty(await till.Db.Invoices.Where(x => x.Channel == SalesChannel.Pos).ToListAsync());
    }

    [Fact]
    public async Task The_walk_in_cannot_be_given_credit()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(100m)]));

        Assert.Contains("walk-in", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_named_customer_carries_the_unsettled_part_and_every_receivable_reader_agrees_with_the_ledger()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        // 248.60 rounds to 249; 100 on card, 149 left on credit.
        var sale = await till.SellAsync(
            session.Id, [till.Momo(1)], [till.Card(100m)], contactId: till.Seed.CustomerId);

        Assert.Equal(249m, sale.GrandTotal);
        Assert.Equal(149m, sale.CreditAmount);

        var receivable = await till.ReceivableAccountIdAsync();
        Assert.Equal(149m, await till.BalanceAsync(receivable));
        Assert.Equal(100m, await till.BalanceAsync(till.BankAccountId));

        var age = await new DocumentAgeQueryHandler(till.Db, till.CurrentUser()).Handle(
            new DocumentAgeQuery(till.OrganizationId, ContactType.Customer, PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        var row = Assert.Single(age.Rows, x => x.DocumentId == sale.Id);
        Assert.Equal(249m, row.Amount);
        Assert.Equal(100m, row.Paid);
        Assert.Equal(149m, row.Balance);

        var statement = await new ContactStatementQueryHandler(till.Db, till.CurrentUser()).Handle(
            new ContactStatementQuery(
                till.OrganizationId, ContactType.Customer, till.Seed.CustomerId,
                PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        Assert.Equal(149m, statement.ClosingBalance);
    }

    [Fact]
    public async Task A_fully_paid_counter_sale_is_outstanding_nowhere_and_leaves_the_walk_in_at_zero()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Card(133m), till.Cash(500m)]);

        var age = await new DocumentAgeQueryHandler(till.Db, till.CurrentUser()).Handle(
            new DocumentAgeQuery(till.OrganizationId, ContactType.Customer, PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        Assert.DoesNotContain(age.Rows, x => x.DocumentId == sale.Id);

        var statement = await new ContactStatementQueryHandler(till.Db, till.CurrentUser()).Handle(
            new ContactStatementQuery(
                till.OrganizationId, ContactType.Customer, till.WalkInId, PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        Assert.Equal(0m, statement.ClosingBalance);
        Assert.Equal(2, statement.Rows.Count(x => x.DocumentType == DocumentType.Invoice));
    }

    [Fact]
    public async Task Credit_needs_the_approve_key_at_the_till_while_a_paid_sale_does_not()
    {
        var till = await PosTestTill.CreateAsync(
            [PermissionKeys.InvoiceCreate, PermissionKeys.PosSessionOperate]);
        var session = await till.OpenAsync();

        await till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)]);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Card(100m)], contactId: till.Seed.CustomerId));
        Assert.Contains(PermissionKeys.InvoiceApprove, refused.Message, StringComparison.Ordinal);
    }

    // ---- Void -------------------------------------------------------------------------------

    [Fact]
    public async Task Void_reverses_both_entries_and_puts_the_stock_back()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        await till.VoidAsync(sale.Id);

        var entries = await till.EntriesForAsync(DocumentType.Invoice, sale.Id);
        Assert.Equal(3, entries.Count);
        var netByAccount = entries.SelectMany(e => e.Lines)
            .GroupBy(x => x.AccountId)
            .Select(g => g.Sum(x => x.Debit - x.Credit));
        Assert.All(netByAccount, net => Assert.Equal(0m, net));

        await StockConservation.AssertHoldsAsync(till.Db, till.OrganizationId);

        // The drawer no longer expects the voided sale's cash.
        var view = await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None);
        Assert.Equal(0, view.Sales.SalesCount);
        Assert.Equal(1000m, view.ExpectedCash);
    }

    [Fact]
    public async Task A_sale_in_a_closed_session_cannot_be_voided()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)]);
        await till.CloseAsync(session.Id, 1249m);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => till.VoidAsync(sale.Id));
        Assert.Contains(session.Code, refused.Message, StringComparison.Ordinal);
        Assert.Equal(InvoiceStatus.Approved, (await till.Db.Invoices.SingleAsync(x => x.Id == sale.Id)).Status);
    }

    // ---- The drawer --------------------------------------------------------------------------

    [Fact]
    public async Task Cash_out_and_the_close_post_and_the_drawer_balances_against_the_ledger()
    {
        var till = await PosTestTill.CreateAsync();
        var openingCash = await till.BalanceAsync(till.CashAccountId);
        var session = await till.OpenAsync(denominations: [new DenominationCount(500, 2)]);
        Assert.Equal(1000m, session.OpeningFloat);

        await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        var afterOut = await till.CashMovementAsync(
            session.Id, PosCashMovementDirection.Out, 100m, till.VegetablesAccountId, "Vegetables");

        // 1000 float + 633 sales - 100 out.
        Assert.Equal(1533m, afterOut.ExpectedCash);
        Assert.Equal(100m, await till.BalanceAsync(till.VegetablesAccountId));

        // Counted 9 short, as the vendor's own drawer was.
        var refused = await Assert.ThrowsAsync<ValidationException>(() => till.CloseAsync(session.Id, 1524m));
        Assert.Contains("short by 9.00", refused.Message, StringComparison.Ordinal);

        var closed = await till.CloseAsync(session.Id, 1524m, "Coins short");
        Assert.Equal(PosSessionStatus.Closed, closed.Status);
        Assert.Equal(1533m, closed.ExpectedCash);
        Assert.Equal(-9m, closed.CashDifference);
        Assert.Equal(9m, await till.BalanceAsync(till.OverShortAccountId));

        // What the ledger holds in the drawer's account moved exactly as the drawer did: the float
        // posts nothing (it was already there), so the account moved by counted minus float.
        Assert.Equal(openingCash + 1524m - 1000m, await till.BalanceAsync(till.CashAccountId));

        var sessionEntries = await till.EntriesForAsync(DocumentType.PosSession, session.Id);
        Assert.Equal(2, sessionEntries.Count);
        Assert.All(sessionEntries, e => Assert.Equal(till.Location.Id, e.LocationId));
    }

    [Fact]
    public async Task A_closed_session_takes_no_more_sales()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        await till.CloseAsync(session.Id, 1000m);

        await Assert.ThrowsAsync<ConflictException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)]));
    }

    [Fact]
    public async Task A_cashier_cannot_sell_in_someone_elses_drawer_or_read_it_without_ViewAll()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        var other = Guid.NewGuid();
        till.Db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(
            till.OrganizationId, other, MembershipRole.Member));
        await till.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)], userId: other));

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetPosSessionQueryHandler(till.Db, till.CurrentUser(other)).Handle(
                new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None));
        Assert.Contains(PermissionKeys.PosSessionViewAll, refused.Message, StringComparison.Ordinal);
    }

    // ---- The one reader -----------------------------------------------------------------------

    /// <summary>
    /// The vendor's Day Report said 610.20 and its session said 611.00 about the same day (defect 7).
    /// Here both come from one reader; this is the test that reads both.
    /// </summary>
    [Fact]
    public async Task The_day_total_equals_the_session_total()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        await till.SellAsync(session.Id, [till.Momo(1), till.Coke(1)], [till.Card(16m), till.Cash(300m)]);
        await till.SellAsync(session.Id, [till.Momo(1)], [till.Card(100m)], contactId: till.Seed.CustomerId);
        var voided = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        await till.VoidAsync(voided.Id);

        var sessionView = await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None);
        var day = await new GetPosDaySummaryQueryHandler(till.Db).Handle(
            new GetPosDaySummaryQuery(till.OrganizationId, PosTestTill.Today, till.Location.Id), CancellationToken.None);

        var s = sessionView.Sales;
        var d = day.Sales;
        Assert.Equal(3, s.SalesCount);
        Assert.Equal(s.SalesCount, d.SalesCount);
        Assert.Equal(s.SubTotal, d.SubTotal);
        Assert.Equal(s.ServiceCharge, d.ServiceCharge);
        Assert.Equal(s.Vat, d.Vat);
        Assert.Equal(s.RoundOff, d.RoundOff);
        Assert.Equal(s.GrandTotal, d.GrandTotal);
        Assert.Equal(s.Tendered, d.Tendered);
        Assert.Equal(s.Change, d.Change);
        Assert.Equal(s.Credit, d.Credit);
        Assert.Equal(s.CashSales, d.CashSales);
        Assert.Equal(
            s.Tenders.Select(x => (x.PaymentModeId, x.Amount)),
            d.Tenders.Select(x => (x.PaymentModeId, x.Amount)));

        // ...and both are the invoices themselves: 633 + 316 + 249.
        Assert.Equal(1198m, s.GrandTotal);
        Assert.Equal(149m, s.Credit);
        Assert.Equal(633m + 300m, s.CashSales);
        Assert.Equal(session.Code, Assert.Single(day.Sessions).Code);
    }

    // ---- Opening a session -------------------------------------------------------------------

    [Fact]
    public async Task A_second_session_at_the_same_till_is_refused_while_the_first_is_open()
    {
        var till = await PosTestTill.CreateAsync();
        var first = await till.OpenAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() => till.OpenAsync());
        Assert.Contains(first.Code, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_till_that_requires_cash_verification_takes_a_count_of_notes_it_holds()
    {
        var till = await PosTestTill.CreateAsync();
        var settings = await till.Db.PosLocationSettings.SingleAsync();
        settings.Update(
            PosMode.Retail, true, 10m, null, true, null, cashVerificationRequired: true,
            [1000, 500, 100], null, true, true, true, false);
        await till.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => till.OpenAsync(amount: 1000m));
        await Assert.ThrowsAsync<ValidationException>(() => till.OpenAsync(amount: null, [new DenominationCount(50, 2)]));

        var opened = await till.OpenAsync(amount: null, [new DenominationCount(500, 1), new DenominationCount(100, 3)]);
        Assert.Equal(800m, opened.OpeningFloat);
        Assert.Equal(2, opened.OpeningCount!.Count);
    }

    [Fact]
    public async Task A_till_with_no_cash_mode_has_no_drawer_and_cannot_open()
    {
        var till = await PosTestTill.CreateAsync();
        till.Db.PosLocationPaymentModes.RemoveRange(
            await till.Db.PosLocationPaymentModes.Where(x => x.PaymentModeId == till.CashModeId).ToListAsync());
        await till.Db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() => till.OpenAsync());
        Assert.Contains("no drawer", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_location_that_runs_no_till_cannot_open_one()
    {
        var till = await PosTestTill.CreateAsync();
        till.Location.SetPosMode(PosMode.None);
        await till.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => till.OpenAsync());
    }

    [Fact]
    public async Task Opening_a_drawer_needs_the_right_to_sell_at_that_location()
    {
        var till = await PosTestTill.CreateAsync([PermissionKeys.PosSessionOperate]);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => till.OpenAsync());
        Assert.Contains(PermissionKeys.InvoiceCreate, refused.Message, StringComparison.Ordinal);
    }

    // ---- What a sale refuses -------------------------------------------------------------------

    [Fact]
    public async Task A_product_not_available_for_sale_is_refused_at_the_counter()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var coke = await till.Db.Products.SingleAsync(x => x.Id == till.CokeId);
        coke.Update(
            coke.Name, coke.CategoryId, coke.PrimaryUnitId, coke.HsCode, availableForSale: false, coke.SellingPrice,
            coke.PurchasePrice, coke.VatRate, coke.ReOrderLevel, coke.TrackInventory, coke.IsActive,
            coke.Sku, coke.Barcode, coke.BatchTracking, coke.SerialTracking, coke.ServiceChargeApplicable);
        await till.Db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]));
        Assert.Contains("Coke 250ml", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cash_tender_must_go_into_this_sessions_drawer()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var cash = await till.Db.PaymentModes.SingleAsync(x => x.Id == till.CashModeId);
        cash.Update(cash.Name, true, false, Domain.Configuration.PaymentModeKind.Cash, till.BankAccountId);
        await till.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            till.SellAsync(session.Id, [till.Momo(1)], [till.Cash(249m)]));
    }

    [Fact]
    public async Task The_invoice_detail_carries_the_till_figures()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        var detail = await new GetInvoiceQueryHandler(till.Db).Handle(
            new GetInvoiceQuery(till.OrganizationId, sale.Id), CancellationToken.None);

        Assert.Equal(SalesChannel.Pos, detail.Channel);
        Assert.Equal(633m, detail.GrandTotal);
        var pos = Assert.IsType<InvoicePosSaleDto>(detail.PosSale);
        Assert.Equal(session.Code, pos.SessionCode);
        Assert.Equal(40m, pos.ServiceChargeTotal);
        Assert.Equal(0.20m, pos.RoundOff);
        Assert.Equal(367m, pos.ChangeAmount);
        Assert.Equal("Cash", Assert.Single(pos.Tenders).PaymentModeName);
        Assert.Equal(10m, detail.Lines.Single(x => x.ProductId == till.MomoId).ServiceChargeRate);
    }
}
