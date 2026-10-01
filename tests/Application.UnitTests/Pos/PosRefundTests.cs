using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Pos.Queries.GetPosDaySummary;
using ErpApp.Application.Pos.Queries.GetPosRefundableSale;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Pos.Queries.ListPosSessionRefunds;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;
using ErpApp.Application.Sales.Queries.SalesReturnRegister;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 63 -- refunds at the till, driven through the real handlers on phase 59's written service
/// (the refund there was one Coke: 60 + 7.80 = 68). Each test ends where the money is: the general
/// ledger, the receivable readers, or the drawer.
/// </summary>
public sealed class PosRefundTests
{
    // ---- The refund and its two entries -------------------------------------------------------

    [Fact]
    public async Task A_partial_cash_refund_posts_a_credit_note_entry_and_a_payout_entry_and_takes_cash_from_the_drawer()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        // Phase 59's refund: one Coke, 60 + 7.80 VAT = 67.80, rounded to the nearest rupee.
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        Assert.Equal(68m, refund.GrandTotal);
        Assert.Equal(0.20m, refund.RoundOff);
        Assert.Equal(68m, refund.PaidOut);
        Assert.Equal(0m, refund.ToAccount);

        var entries = await till.EntriesForAsync(DocumentType.CreditNote, refund.Id);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(e.Lines.Sum(x => x.Debit), e.Lines.Sum(x => x.Credit)));

        var receivable = await till.ReceivableAccountIdAsync();
        Assert.Equal(0m, await till.BalanceAsync(receivable));
        Assert.Equal(633m - 68m, await till.BalanceAsync(till.CashAccountId));
        // The sale's +0.20 and the refund's +0.20 are posted in opposite directions.
        Assert.Equal(0m, await till.BalanceAsync(till.RoundingAccountId));

        var note = await till.Db.CreditNotes.Include(x => x.Payouts).SingleAsync(x => x.Id == refund.Id);
        Assert.Equal(SalesChannel.Pos, note.Channel);
        Assert.Equal(CreditNoteStatus.Approved, note.Status);
        Assert.Equal(session.Id, note.PosSessionId);
        Assert.Equal(till.Location.Id, note.LocationId);
        Assert.Equal(sale.Id, note.ReferrerId);
        Assert.Equal(sale.Code, note.Reference);
        Assert.Equal("Customer changed their mind", note.Reason);
        Assert.Single(note.Payouts);

        var view = await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None);
        Assert.Equal(1000m + 633m - 68m, view.ExpectedCash);
        Assert.Equal(1, view.Sales.Refunds.RefundsCount);
        Assert.Equal(68m, view.Sales.Refunds.CashRefunds);
        Assert.Equal(633m - 68m, view.Sales.NetSales);

        // Returned at the cost the goods left at (phase 37): the three views still agree.
        await StockConservation.AssertHoldsAsync(till.Db, till.OrganizationId);
    }

    [Fact]
    public async Task Returning_a_service_charged_line_gives_back_its_service_charge_and_the_VAT_on_it()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        // One momo of two: 200 + 20 service charge + 13% of 220 = 248.60, rounded to 249.
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)], [till.Cash(249m)]);

        Assert.Equal(20m, refund.ServiceCharge);
        Assert.Equal(249m, refund.GrandTotal);
        Assert.Equal(-20m, await till.BalanceAsync(till.ServiceChargeAccountId));

        var line = Assert.Single(await till.Db.CreditNoteLines.Where(x => x.CreditNoteId == refund.Id).ToListAsync());
        Assert.Equal(10m, line.ServiceChargeRate);
        Assert.Equal(28.60m, line.VatAmount);
    }

    [Fact]
    public async Task A_bill_refunded_in_two_parts_gives_back_exactly_what_it_charged()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        // 316.40 rounds down to 316; the second refund returns the last of the bill, so it gives back
        // exactly what is left of 633 -- 317 -- rather than rounding 316.40 down again.
        var first = await till.RefundAsync(
            session.Id, sale.Id,
            [await till.ReturnAsync(sale.Id, till.MomoId, 1m), await till.ReturnAsync(sale.Id, till.CokeId, 1m)],
            [till.Cash(316m)]);
        var second = await till.RefundAsync(
            session.Id, sale.Id,
            [await till.ReturnAsync(sale.Id, till.MomoId, 1m), await till.ReturnAsync(sale.Id, till.CokeId, 1m)],
            [till.Cash(317m)]);

        Assert.Equal(316m, first.GrandTotal);
        Assert.Equal(317m, second.GrandTotal);
        Assert.Equal(0.60m, second.RoundOff);

        // Every account the sale touched is back where it started.
        Assert.Equal(0m, await till.BalanceAsync(await till.ReceivableAccountIdAsync()));
        Assert.Equal(0m, await till.BalanceAsync(till.CashAccountId));
        Assert.Equal(0m, await till.BalanceAsync(till.ServiceChargeAccountId));
        Assert.Equal(0m, await till.BalanceAsync(till.RoundingAccountId));
        await StockConservation.AssertHoldsAsync(till.Db, till.OrganizationId);
    }

    // ---- What is handed back ---------------------------------------------------------------

    [Fact]
    public async Task The_walk_in_is_paid_back_exactly_the_refund_never_more_and_never_less()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(2)], [till.Cash(136m)]);
        var coke = await till.ReturnAsync(sale.Id, till.CokeId, 1m);

        var short1 = await Assert.ThrowsAsync<ValidationException>(() =>
            till.RefundAsync(session.Id, sale.Id, [coke], [till.Cash(50m)]));
        Assert.Contains("exactly 68.00", short1.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ValidationException>(() =>
            till.RefundAsync(session.Id, sale.Id, [coke], [till.Cash(100m)]));

        Assert.Empty(await till.Db.CreditNotes.ToListAsync());
    }

    [Fact]
    public async Task A_part_credit_sale_refund_clears_what_is_owed_first_and_pays_back_only_what_was_paid()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var customer = till.Seed.CustomerId;

        // 249, of which 100 on card and 149 left on the customer's account (phase 61's own case).
        var sale = await till.SellAsync(session.Id, [till.Momo(1)], [till.Card(100m)], contactId: customer);
        Assert.Equal(149m, sale.CreditAmount);

        var preview = await till.PreviewRefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)]);
        Assert.Equal(249m, preview.GrandTotal);
        Assert.Equal(149m, preview.OwedBefore);
        Assert.Equal(100m, preview.RequiredPayout);
        Assert.Equal(149m, preview.ToAccount);

        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)], [till.Cash(100m)]);
        Assert.Equal(100m, refund.PaidOut);
        Assert.Equal(149m, refund.ToAccount);

        Assert.Equal(0m, await till.BalanceAsync(await till.ReceivableAccountIdAsync()));

        // Every reader of what the customer owes agrees with the ledger: nothing.
        var age = await new DocumentAgeQueryHandler(till.Db, till.CurrentUser()).Handle(
            new DocumentAgeQuery(till.OrganizationId, ContactType.Customer, PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        Assert.DoesNotContain(age.Rows, x => x.ContactId == customer);

        var statement = await new ContactStatementQueryHandler(till.Db, till.CurrentUser()).Handle(
            new ContactStatementQuery(
                till.OrganizationId, ContactType.Customer, customer, PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        Assert.Equal(0m, statement.ClosingBalance);
    }

    [Fact]
    public async Task A_credit_sale_refund_goes_to_the_customers_account_and_nothing_is_paid_out()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var customer = till.Seed.CustomerId;

        var sale = await till.SellAsync(session.Id, [till.Momo(1)], [], contactId: customer);
        Assert.Equal(249m, sale.CreditAmount);

        var momo = await till.ReturnAsync(sale.Id, till.MomoId, 1m);
        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            till.RefundAsync(session.Id, sale.Id, [momo], [till.Cash(249m)]));
        Assert.Contains("Nothing is handed back", refused.Message, StringComparison.Ordinal);

        var refund = await till.RefundAsync(session.Id, sale.Id, [momo], []);
        Assert.Equal(0m, refund.PaidOut);
        Assert.Equal(249m, refund.ToAccount);
        Assert.Single(await till.EntriesForAsync(DocumentType.CreditNote, refund.Id));

        Assert.Equal(0m, await till.BalanceAsync(await till.ReceivableAccountIdAsync()));
        var statement = await new ContactStatementQueryHandler(till.Db, till.CurrentUser()).Handle(
            new ContactStatementQuery(
                till.OrganizationId, ContactType.Customer, customer, PosTestTill.Today.AddDays(-1), PosTestTill.Today),
            CancellationToken.None);
        Assert.Equal(0m, statement.ClosingBalance);
    }

    [Fact]
    public async Task Cash_paid_out_cannot_exceed_what_the_drawer_should_hold()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync(amount: 0m);
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Card(68m)]);
        var coke = await till.ReturnAsync(sale.Id, till.CokeId, 1m);

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            till.RefundAsync(session.Id, sale.Id, [coke], [till.Cash(68m)]));
        Assert.Contains("should hold 0.00", refused.Message, StringComparison.Ordinal);

        // Back onto the card it was paid with, which no drawer holds.
        var refund = await till.RefundAsync(session.Id, sale.Id, [coke], [till.Card(68m)]);
        Assert.Equal(68m, refund.PaidOut);
        Assert.Equal(0m, await till.BalanceAsync(till.BankAccountId));
    }

    // ---- Where, and how much ---------------------------------------------------------------

    [Fact]
    public async Task A_sale_from_a_closed_session_is_refunded_into_the_cashiers_open_session()
    {
        var till = await PosTestTill.CreateAsync();
        var yesterday = await till.OpenAsync();
        var sale = await till.SellAsync(yesterday.Id, [till.Coke(1)], [till.Cash(68m)]);
        await till.CloseAsync(yesterday.Id, 1068m);

        var today = await till.OpenAsync();
        var refund = await till.RefundAsync(
            today.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        var note = await till.Db.CreditNotes.SingleAsync(x => x.Id == refund.Id);
        Assert.Equal(today.Id, note.PosSessionId);

        var handler = new GetPosSessionQueryHandler(till.Db, till.CurrentUser());
        var todayView = await handler.Handle(new GetPosSessionQuery(till.OrganizationId, today.Id), CancellationToken.None);
        var yesterdayView = await handler.Handle(new GetPosSessionQuery(till.OrganizationId, yesterday.Id), CancellationToken.None);
        Assert.Equal(1000m - 68m, todayView.ExpectedCash);
        Assert.Equal(1068m, yesterdayView.ExpectedCash);
        Assert.Equal(0, yesterdayView.Sales.Refunds.RefundsCount);
    }

    [Fact]
    public async Task A_line_is_refunded_at_most_what_was_sold_less_earlier_refunds()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(2)], [till.Cash(136m)]);

        await Assert.ThrowsAsync<ConflictException>(async () => await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 3m)], [till.Cash(203m)]));

        await till.RefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 2m)], [till.Cash(136m)]);

        await Assert.ThrowsAsync<ConflictException>(async () => await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]));

        var refundable = await new GetPosRefundableSaleQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosRefundableSaleQuery(till.OrganizationId, sale.Id), CancellationToken.None);
        var line = Assert.Single(refundable.Lines);
        Assert.Equal(2m, line.Sold);
        Assert.Equal(0m, line.Remaining);
        Assert.Single(refundable.PriorRefunds);
    }

    [Fact]
    public async Task A_refund_needs_credit_note_approve_at_the_location_and_names_the_key()
    {
        var keys = PosTestTill.CashierKeys.Where(x => x != PermissionKeys.CreditNoteApprove).ToArray();
        var till = await PosTestTill.CreateAsync(keys);
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(async () => await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]));
        Assert.Contains(PermissionKeys.CreditNoteApprove, refused.Message, StringComparison.Ordinal);

        var refundable = await new GetPosRefundableSaleQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosRefundableSaleQuery(till.OrganizationId, sale.Id), CancellationToken.None);
        Assert.False(refundable.CanRefund);
    }

    [Fact]
    public void A_refund_is_lock_date_sensitive_audited_and_metered()
    {
        // LockDateBehavior refuses a refund dated inside a locked period, as it does any credit note.
        Assert.True(typeof(ILockDateSensitive).IsAssignableFrom(typeof(CreatePosRefundCommand)));
        Assert.True(typeof(IAuditableRequest).IsAssignableFrom(typeof(CreatePosRefundCommand)));
        Assert.True(typeof(IMeteredTransaction).IsAssignableFrom(typeof(CreatePosRefundCommand)));
        Assert.StartsWith("Create", nameof(CreatePosRefundCommand), StringComparison.Ordinal);
    }

    // ---- The ERP's door, and voids ---------------------------------------------------------

    [Fact]
    public async Task The_erp_cannot_raise_a_credit_note_against_a_till_sale()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);

        var create = await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateCreditNoteCommandHandler(till.Db).Handle(
                new CreateCreditNoteCommand(
                    till.OrganizationId, till.WalkInId, PosTestTill.Today, null,
                    [new CreditNoteLineInput(till.CokeId, 1m, 60m, VatRate.ThirteenPercentVat)],
                    DocumentType.Invoice, sale.Id),
                CancellationToken.None));
        Assert.Contains("returned at the till", create.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new GetCreditNoteConversionTemplateQueryHandler(till.Db).Handle(
                new GetCreditNoteConversionTemplateQuery(till.OrganizationId, sale.Id), CancellationToken.None));
    }

    [Fact]
    public async Task A_refund_is_voidable_while_its_session_is_open_and_puts_the_cash_back()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(2)], [till.Cash(136m)]);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        await till.VoidRefundAsync(refund.Id);

        Assert.Equal(136m, await till.BalanceAsync(till.CashAccountId));
        Assert.Equal(0m, await till.BalanceAsync(await till.ReceivableAccountIdAsync()));
        var view = await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None);
        Assert.Equal(1136m, view.ExpectedCash);
        Assert.Equal(0, view.Sales.Refunds.RefundsCount);
        await StockConservation.AssertHoldsAsync(till.Db, till.OrganizationId);

        // The voided refund frees its quantity again (phase 6's cap counts non-void notes only).
        await till.RefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 2m)], [till.Cash(136m)]);
    }

    [Fact]
    public async Task A_refund_of_a_closed_session_cannot_be_voided()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(2)], [till.Cash(136m)]);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);
        await till.CloseAsync(session.Id, 1068m);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => till.VoidRefundAsync(refund.Id));
        Assert.Contains("closed and counted", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refund_whose_payout_is_reconciled_cannot_be_voided()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Card(68m)]);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Card(68m)]);

        var bankLine = (await till.EntriesForAsync(DocumentType.CreditNote, refund.Id))
            .SelectMany(x => x.Lines)
            .Single(x => x.AccountId == till.BankAccountId);
        bankLine.Reconcile(Guid.NewGuid());
        await till.Db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() => till.VoidRefundAsync(refund.Id));
        Assert.Contains("bank reconciliation", refused.Message, StringComparison.Ordinal);
    }

    // ---- One reader, the receipt, and the ERP's view ------------------------------------------

    [Fact]
    public async Task The_day_total_equals_the_session_total_with_refunds()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        await till.RefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);
        await till.RefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)], [till.Card(249m)]);

        var s = (await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None)).Sales.Refunds;
        var d = (await new GetPosDaySummaryQueryHandler(till.Db).Handle(
            new GetPosDaySummaryQuery(till.OrganizationId, PosTestTill.Today, till.Location.Id), CancellationToken.None))
            .Sales.Refunds;

        Assert.Equal(2, s.RefundsCount);
        Assert.Equal(317m, s.GrandTotal);
        Assert.Equal(68m, s.CashRefunds);
        Assert.Equal(317m, s.PaidOut);
        Assert.Equal(s, d with { Payouts = s.Payouts });
        Assert.Equal(s.Payouts, d.Payouts);
    }

    [Fact]
    public async Task A_refund_receipt_names_the_sale_and_counts_its_prints()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)], "Damaged can");

        var original = await till.PrintRefundAsync(refund.Id);
        var copy = await till.PrintRefundAsync(refund.Id);

        Assert.Equal(1, original.PrintNumber);
        Assert.Equal(2, copy.PrintNumber);
        Assert.Equal(sale.Code, original.InvoiceCode);
        Assert.Equal(PosTestTill.Today, original.InvoiceDate);
        Assert.Equal("Damaged can", original.Reason);
        Assert.Equal(68m, original.GrandTotal);
        Assert.Equal(7.80m, original.Vat);
        Assert.Equal(60m, original.TaxableAmount);
        Assert.True(original.IsWalkIn);
        Assert.Single(original.Payouts);

        var listed = await new ListPosSessionRefundsQueryHandler(till.Db, till.CurrentUser()).Handle(
            new ListPosSessionRefundsQuery(till.OrganizationId, session.Id), CancellationToken.None);
        var row = Assert.Single(listed);
        Assert.Equal(2, row.PrintCount);
        Assert.Equal(sale.Code, row.InvoiceCode);
    }

    [Fact]
    public async Task The_erp_detail_shows_the_refunds_own_figures_and_the_return_register_carries_its_service_charge()
    {
        var keys = PosTestTill.CashierKeys.Append(PermissionKeys.SalesReturnRegisterView).ToArray();
        var till = await PosTestTill.CreateAsync(keys);
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2)], [till.Cash(497m)]);
        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)], [till.Cash(249m)]);

        var detail = await new GetCreditNoteQueryHandler(till.Db).Handle(
            new GetCreditNoteQuery(till.OrganizationId, refund.Id), CancellationToken.None);
        Assert.NotNull(detail.PosRefund);
        Assert.Equal(249m, detail.PosRefund!.GrandTotal);
        Assert.Equal(20m, detail.PosRefund.ServiceChargeTotal);
        Assert.Equal(0.40m, detail.PosRefund.RoundOff);
        Assert.Equal(20m, Assert.Single(detail.Lines).ServiceChargeAmount);

        var register = await new SalesReturnRegisterQueryHandler(till.Db, till.CurrentUser()).Handle(
            new SalesReturnRegisterQuery(till.OrganizationId, PosTestTill.Today, PosTestTill.Today, null),
            CancellationToken.None);
        var line = Assert.Single(register.Items);
        // Taxable value includes the service charge (it is inside the VAT base); the round-off is in no line.
        Assert.Equal(220m, line.TaxableReturnValue);
        Assert.Equal(28.60m, line.VatAmount);
        Assert.Equal(248.60m, line.TotalReturnValue);
    }
}
