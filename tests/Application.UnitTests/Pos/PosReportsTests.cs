using ErpApp.Application.Pos.Queries.GetPosDashboard;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Pos.Queries.ListPosSessions;
using ErpApp.Application.Pos.Queries.PosOrderReport;
using ErpApp.Application.Pos.Queries.PosPaymentSummary;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Application.Sales.Queries.SalesMasterReport;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Trade;
using ErpApp.Application.Trade.Queries.SalesSummaryReport;
using ErpApp.Application.Trade.Queries.TradeByContact;
using ErpApp.Application.Trade.Queries.TradeByItem;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Queries.SystemAuditReport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 66 -- the POS reports and the dashboard, each read against the session/day reader and the Sales
/// Register on the same documents (phase 36's rule: two figures agree only through one shared reader plus a
/// test that reads both). The vendor printed 611, 610.20 and 542.40 for one day; every test here asserts one
/// figure from two sides.
/// </summary>
public sealed class PosReportsTests
{
    private static DateOnly Today => PosTestTill.Today;

    /// <summary>
    /// A day at one till: three sales (one with change, one split across card and cash, one part on a named
    /// customer's credit), a fourth sale voided, and a refund of one momo paid out in cash -- plus one ERP
    /// invoice the same day, which the Channel filter must keep out. The numbers are chosen so the round-off
    /// does not cancel: +0.20, -0.40 and +0.40 on the sales, +0.40 on the refund, net -0.20.
    /// </summary>
    private sealed record Day(PosTestTill Till, PosSessionDto Session, Guid ErpInvoiceId);

    private static async Task<Day> RingUpADayAsync()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        var first = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        await till.SellAsync(session.Id, [till.Momo(1), till.Coke(1)], [till.Card(16m), till.Cash(300m)]);
        await till.SellAsync(session.Id, [till.Momo(1)], [till.Card(100m)], contactId: till.Seed.CustomerId);
        var voided = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        await till.VoidAsync(voided.Id);
        await till.RefundAsync(session.Id, first.Id, [await till.ReturnAsync(first.Id, till.MomoId, 1m)], [till.Cash(249m)]);

        var erp = await InventoryReportSeed.SellAsync(
            till.Db, till.Seed, Today, 1m, 500m, till.Seed.ProductId, VatRate.ThirteenPercentVat);

        var refreshed = await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None);

        return new Day(till, refreshed, erp.Id);
    }

    private static Task<PosDayReportDto> DayReportAsync(PosTestTill till, Guid? locationId = null) =>
        new GetPosDayReportQueryHandler(till.Db).Handle(
            new GetPosDayReportQuery(till.OrganizationId, Today, Today, locationId), CancellationToken.None);

    // ---- The Day Report against the session and the Sales Register -------------------------------------------

    [Fact]
    public async Task The_day_report_is_the_reader_and_differs_from_the_sales_register_by_the_net_round_off_alone()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;
        var report = await DayReportAsync(till, till.Location.Id);
        var s = report.Sales;

        // The session's X report and the day are one reader (phase 61 K), now over a period.
        Assert.Equal(day.Session.Sales, s with { Tenders = day.Session.Sales.Tenders, Refunds = day.Session.Sales.Refunds });
        Assert.Equal(day.Session.Sales.Refunds, s.Refunds with { Payouts = day.Session.Sales.Refunds.Payouts });

        // 633 + 316 + 249, less the 249 refund; the voided 68 is nowhere in it, and is counted apart.
        Assert.Equal(3, s.SalesCount);
        Assert.Equal(1198m, s.GrandTotal);
        Assert.Equal(249m, s.Refunds.GrandTotal);
        Assert.Equal(949m, s.NetSales);
        Assert.Equal(-0.20m, s.NetRoundOff);
        Assert.Equal(1, report.VoidedSales);
        Assert.Equal(day.Session.Code, Assert.Single(report.Sessions).Code);

        // The Sales Register lists supplies and never a round-off: net sales less the net round-off.
        var register = await new SalesRegisterQueryHandler(till.Db, till.CurrentUser()).Handle(
            new SalesRegisterQuery(
                till.OrganizationId, Today, Today, null, null, LocationId: till.Location.Id, Channel: SalesChannel.Pos),
            CancellationToken.None);
        Assert.Equal(949.20m, register.TotalValue);
        Assert.Equal(s.NetSales - s.NetRoundOff, register.TotalValue);
        Assert.Equal(s.Taxable - s.Refunds.Taxable, register.TotalTaxableValue);
        Assert.Equal(s.NonTaxable - s.Refunds.NonTaxable, register.TotalTaxExemptValue);
        Assert.Equal(s.Vat - s.Refunds.Vat, register.TotalVatAmount);

        // The channel narrows both halves: the whole register also holds the ERP invoice (500 + 65 VAT,
        // at the same head office), and the refund is in both.
        var whole = await new SalesRegisterQueryHandler(till.Db, till.CurrentUser()).Handle(
            new SalesRegisterQuery(till.OrganizationId, Today, Today, null, null, LocationId: till.Location.Id),
            CancellationToken.None);
        Assert.Equal(register.TotalValue + 565m, whole.TotalValue);
        Assert.Equal(
            whole.Items.Count(x => x.DocumentType == DocumentType.CreditNote),
            register.Items.Count(x => x.DocumentType == DocumentType.CreditNote));
        var erpOnly = await new SalesRegisterQueryHandler(till.Db, till.CurrentUser()).Handle(
            new SalesRegisterQuery(till.OrganizationId, Today, Today, null, null, Channel: SalesChannel.Erp),
            CancellationToken.None);
        Assert.Equal(565m, erpOnly.TotalValue);
        Assert.DoesNotContain(erpOnly.Items, x => x.DocumentType == DocumentType.CreditNote);
    }

    [Fact]
    public async Task The_payments_breakdown_adds_up_to_net_sales_mode_by_mode()
    {
        var day = await RingUpADayAsync();
        var report = await DayReportAsync(day.Till);
        var payments = report.Payments;
        var s = report.Sales;

        Assert.Equal(s.NetSales, payments.Total);
        Assert.Equal(s.Change, payments.Change);
        Assert.Equal(s.NetCredit, payments.Credit);

        // Cash: 1000 + 300 received, 249 paid back, less the 367 change -- what the drawer kept.
        var cash = payments.Modes.Single(x => x.PaymentModeId == day.Till.CashModeId);
        Assert.Equal(1300m, cash.Received);
        Assert.Equal(249m, cash.PaidBack);
        Assert.Equal(s.NetCash, cash.Net - payments.Change);
        Assert.Equal(116m, payments.Modes.Single(x => x.PaymentModeId == day.Till.CardModeId).Net);
        Assert.Equal(149m, payments.Credit);
    }

    [Fact]
    public async Task A_day_report_over_a_period_is_the_sum_of_its_days_and_its_series_adds_up_to_it()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;

        var hourly = await DayReportAsync(till);
        Assert.Equal(PosSalesBucket.Hour, hourly.Bucket);
        Assert.Equal(24, hourly.Series.Count);
        Assert.Equal(hourly.Sales.NetSales, hourly.Series.Sum(x => x.Net));
        Assert.Equal(hourly.Sales.GrandTotal, hourly.Series.Sum(x => x.Sales));
        Assert.Equal(hourly.Sales.SalesCount, hourly.Series.Sum(x => x.SalesCount));

        var week = await new GetPosDayReportQueryHandler(till.Db).Handle(
            new GetPosDayReportQuery(till.OrganizationId, Today.AddDays(-6), Today), CancellationToken.None);
        Assert.Equal(PosSalesBucket.Day, week.Bucket);
        Assert.Equal(7, week.Series.Count);
        Assert.Equal(Today, week.Series[^1].Date);
        Assert.Equal(hourly.Sales.NetSales, week.Sales.NetSales);
        Assert.Equal(week.Sales.NetSales, week.Series.Sum(x => x.Net));
        Assert.All(week.Series.SkipLast(1), x => Assert.Equal(0m, x.Net));
    }

    // ---- The ERP reports read with Channel = POS ------------------------------------------------------------

    [Fact]
    public async Task Sales_by_item_customer_summary_and_master_with_channel_pos_total_what_the_register_totals()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;
        var user = till.CurrentUser();
        var s = (await DayReportAsync(till)).Sales;
        var registerTotal = s.NetSales - s.NetRoundOff;

        var byItem = await new TradeByItemQueryHandler(till.Db, user).Handle(
            new TradeByItemQuery(till.OrganizationId, TradeSide.Sales, Today, Today, Channel: SalesChannel.Pos),
            CancellationToken.None);
        Assert.Equal(registerTotal, byItem.TotalTotalAmount);
        Assert.Equal(s.ServiceCharge - s.Refunds.ServiceCharge, byItem.TotalServiceCharge);
        Assert.Equal(byItem.TotalNetAmount + byItem.TotalServiceCharge + byItem.TotalVatAmount, byItem.TotalTotalAmount);

        // The filter bites: the ERP invoice of 565 is in the unfiltered report and not in the till's.
        var everything = await new TradeByItemQueryHandler(till.Db, user).Handle(
            new TradeByItemQuery(till.OrganizationId, TradeSide.Sales, Today, Today), CancellationToken.None);
        Assert.Equal(registerTotal + 565m, everything.TotalTotalAmount);

        var byCustomer = await new TradeByContactQueryHandler(till.Db, user).Handle(
            new TradeByContactQuery(till.OrganizationId, TradeSide.Sales, Today, Today, Channel: SalesChannel.Pos),
            CancellationToken.None);
        Assert.Equal(registerTotal, byCustomer.TotalTotalAmount);
        Assert.Equal(byItem.TotalServiceCharge, byCustomer.TotalServiceCharge);

        var fiscalYear = BsCalendar.FiscalYearOf(Today)!.Value;
        var summary = await new SalesSummaryReportQueryHandler(till.Db, user).Handle(
            new SalesSummaryReportQuery(till.OrganizationId, fiscalYear, SalesSummaryMode.Date, Channel: SalesChannel.Pos),
            CancellationToken.None);
        var todayRow = Assert.Single(summary.Rows);
        Assert.Equal(registerTotal, todayRow.Total);
        Assert.Equal(byItem.TotalServiceCharge, todayRow.ServiceCharge);
        Assert.Equal(todayRow.SubTotal - todayRow.Discount + todayRow.ServiceCharge, todayRow.NonTaxableSales + todayRow.TaxableSales);

        var master = await new SalesMasterReportQueryHandler(till.Db, user).Handle(
            new SalesMasterReportQuery(till.OrganizationId, Today, Today, null, null, null, Channel: SalesChannel.Pos),
            CancellationToken.None);
        Assert.Equal(registerTotal, master.NetTotal);
        Assert.Equal(s.GrandTotal - s.RoundOff, master.SalesTotal);
        Assert.All(master.Rows, x => Assert.Equal(day.Session.UserName, x.Cashier));
        Assert.Contains(master.Rows, x => x.Type == DocumentType.Invoice && x.PaymentModes == "Card, Credit");
        Assert.Contains(master.Rows, x => x.Type == DocumentType.CreditNote && x.PaymentModes == "Cash");
    }

    /// <summary>Phase 64's lesson: a validator nothing constructs can 500 every request it guards. These
    /// four gained a channel rule this phase and are built and run here with one.</summary>
    [Fact]
    public async Task The_reports_given_a_channel_build_their_validators_and_take_it()
    {
        var org = Guid.NewGuid();
        Assert.True((await new SalesMasterReportQueryValidator().ValidateAsync(
            new SalesMasterReportQuery(org, Today, Today, null, null, null, Channel: SalesChannel.Pos))).IsValid);
        Assert.True((await new SalesRegisterQueryValidator().ValidateAsync(
            new SalesRegisterQuery(org, Today, Today, null, null, Channel: SalesChannel.Pos))).IsValid);
        Assert.True((await new SalesSummaryReportQueryValidator().ValidateAsync(
            new SalesSummaryReportQuery(org, 2083, Channel: SalesChannel.Pos))).IsValid);
        Assert.True((await new SystemAuditReportQueryValidator().ValidateAsync(
            new SystemAuditReportQuery(org, null, null, null, Today, Today, Channel: SalesChannel.Pos))).IsValid);
        Assert.False((await new SalesRegisterQueryValidator().ValidateAsync(
            new SalesRegisterQuery(org, Today, Today, null, null, Channel: (SalesChannel)7))).IsValid);
    }

    [Fact]
    public async Task Channel_applies_to_sales_only()
    {
        var validator = new TradeByItemQueryValidator();
        var purchase = await validator.ValidateAsync(new TradeByItemQuery(
            Guid.NewGuid(), TradeSide.Purchase, Today, Today, Channel: SalesChannel.Pos));
        Assert.Contains(purchase.Errors, x => x.PropertyName == nameof(TradeByItemQuery.Channel));

        var contactValidator = new TradeByContactQueryValidator();
        var supplier = await contactValidator.ValidateAsync(new TradeByContactQuery(
            Guid.NewGuid(), TradeSide.Purchase, Today, Today, Channel: SalesChannel.Pos));
        Assert.Contains(supplier.Errors, x => x.PropertyName == nameof(TradeByContactQuery.Channel));
    }

    // ---- The Payment Summary ------------------------------------------------------------------------------

    [Fact]
    public async Task The_payment_summary_rows_sum_to_net_sales_and_each_type_to_the_readers_figure()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;
        var s = (await DayReportAsync(till)).Sales;

        var summary = await new PosPaymentSummaryQueryHandler(till.Db, till.CurrentUser()).Handle(
            new PosPaymentSummaryQuery(till.OrganizationId, Today, Today, ExportAll: true), CancellationToken.None);

        Assert.Equal(s.NetSales, summary.Total);
        Assert.Equal(s.NetSales, summary.Items.Sum(x => x.Amount));
        Assert.Equal(s.NetCash, summary.Totals.Single(x => x.Type == PosPaymentType.Cash).Amount);
        Assert.Equal(s.NetCredit, summary.Totals.Single(x => x.Type == PosPaymentType.Credit).Amount);
        Assert.Equal(116m, summary.Totals.Single(x => x.Type == PosPaymentType.Card).Amount);

        // The change and the credit are rows of their own, and the voided sale has none.
        Assert.Contains(summary.Items, x => x.Entry == PosPaymentEntry.Change && x.Amount == -367m);
        Assert.Contains(summary.Items, x => x.Entry == PosPaymentEntry.Credit && x.Amount == 149m);
        Assert.Contains(summary.Items, x => x.Entry == PosPaymentEntry.Payout && x.Amount == -249m);
        // Two rows a sale (tender and change; card and cash; card and credit) and the payout.
        Assert.Equal(7, summary.TotalCount);

        // Filtering by mode keeps the mode's rows, and its total is the mode's net less its change.
        var cash = await new PosPaymentSummaryQueryHandler(till.Db, till.CurrentUser()).Handle(
            new PosPaymentSummaryQuery(till.OrganizationId, Today, Today, PaymentModeId: till.CashModeId),
            CancellationToken.None);
        Assert.Equal(s.NetCash, cash.Total);
    }

    [Fact]
    public void Every_payment_mode_kind_is_a_payment_type_by_name()
    {
        foreach (var kind in Enum.GetValues<PaymentModeKind>())
        {
            Assert.Equal(kind.ToString(), PosPaymentSummaryQueryHandler.TypeOf(kind).ToString());
        }

        // The two enums number their members differently, which is why the mapping is by name.
        Assert.NotEqual((int)PaymentModeKind.Cash, (int)PosPaymentType.Cash);
    }

    // ---- The dashboard ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_dashboard_is_the_day_report_and_its_products_and_round_off_add_up_to_its_net_sales()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;
        var report = await DayReportAsync(till);

        var dashboard = await new GetPosDashboardQueryHandler(till.Db).Handle(
            new GetPosDashboardQuery(till.OrganizationId, Today, Today), CancellationToken.None);

        Assert.Equal(report.Sales with { Tenders = [], Refunds = PosRefundsSummaryDto.None },
            dashboard.Sales with { Tenders = [], Refunds = PosRefundsSummaryDto.None });
        Assert.Equal(report.Sales.NetSales, dashboard.Payments.Total);
        Assert.Equal(report.Series.Select(x => x.Net), dashboard.Series.Select(x => x.Net));

        // The vendor's panel totalled 610.20 under a 611 tile; ours prints the round-off as a row.
        Assert.Equal(
            dashboard.Sales.NetSales,
            dashboard.TopProducts.Sum(x => x.Total) + dashboard.OtherProducts + dashboard.Sales.NetRoundOff);
        Assert.Equal("Chicken Momo", dashboard.TopProducts[0].Name);

        // ...and each product is Sales by Item's own row.
        var byItem = await new TradeByItemQueryHandler(till.Db, till.CurrentUser()).Handle(
            new TradeByItemQuery(till.OrganizationId, TradeSide.Sales, Today, Today, Channel: SalesChannel.Pos),
            CancellationToken.None);
        Assert.All(dashboard.TopProducts, p => Assert.Equal(byItem.Rows.Single(r => r.Id == p.ProductId).TotalAmount, p.Total));

        Assert.Equal(day.Session.Code, Assert.Single(dashboard.OpenSessions).Code);
    }

    // ---- The sessions list ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_sessions_row_carries_its_x_reports_totals_and_its_close()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;
        await till.CloseAsync(day.Session.Id, day.Session.ExpectedCash - 5m, "Five short");

        var list = await new ListPosSessionsQueryHandler(till.Db).Handle(
            new ListPosSessionsQuery(till.OrganizationId, Today, Today), CancellationToken.None);

        var row = Assert.Single(list.Items);
        Assert.Equal(day.Session.Sales.SalesCount, row.SalesCount);
        Assert.Equal(day.Session.Sales.GrandTotal, row.Sales);
        Assert.Equal(day.Session.Sales.Refunds.GrandTotal, row.Refunds);
        Assert.Equal(PosSessionStatus.Closed, row.Status);
        Assert.Equal(-5m, row.CashDifference);

        var tomorrow = await new ListPosSessionsQueryHandler(till.Db).Handle(
            new ListPosSessionsQuery(till.OrganizationId, Today.AddDays(1), Today.AddDays(1)), CancellationToken.None);
        Assert.Empty(tomorrow.Items);

        // The list's search box: a session code, or the cashier's name (stored casing -- phase 34b).
        Assert.Single((await new ListPosSessionsQueryHandler(till.Db).Handle(
            new ListPosSessionsQuery(till.OrganizationId, Today, Today, Search: day.Session.Code), CancellationToken.None)).Items);
        Assert.Single((await new ListPosSessionsQueryHandler(till.Db).Handle(
            new ListPosSessionsQuery(till.OrganizationId, Today, Today, Search: "Sita"), CancellationToken.None)).Items);
        Assert.Empty((await new ListPosSessionsQueryHandler(till.Db).Handle(
            new ListPosSessionsQuery(till.OrganizationId, Today, Today, Search: "Nobody"), CancellationToken.None)).Items);
    }

    // ---- POS activity -------------------------------------------------------------------------------------

    [Fact]
    public async Task System_audit_with_channel_pos_keeps_the_tills_documents_and_cuts_days_on_the_nepal_clock()
    {
        var day = await RingUpADayAsync();
        var till = day.Till;
        var db = (DbContext)till.Db;

        var sale = await till.Db.Invoices.FirstAsync(x => x.Channel == SalesChannel.Pos);
        var refund = await till.Db.CreditNotes.FirstAsync(x => x.Channel == SalesChannel.Pos);
        var order = Guid.NewGuid();

        // 00:30 in Nepal is 18:45 UTC the day before: the row belongs to today's page, not yesterday's.
        var justAfterMidnight = new DateTimeOffset(Today.ToDateTime(new TimeOnly(0, 30)), NepalTime.Offset);
        var rows = new[]
        {
            Audit.Create(till.OrganizationId, till.UserId, "Create", DocumentType.Invoice, sale.Id),
            Audit.Create(till.OrganizationId, till.UserId, "Create", DocumentType.CreditNote, refund.Id),
            Audit.Create(till.OrganizationId, till.UserId, "Void", DocumentType.PosOrder, order),
            Audit.Create(till.OrganizationId, till.UserId, "Approve", DocumentType.Invoice, day.ErpInvoiceId),
        };
        till.Db.Audits.AddRange(rows);
        await till.Db.SaveChangesAsync();
        foreach (var row in rows)
        {
            db.Entry(row).Property(nameof(Audit.CreatedAt)).CurrentValue = justAfterMidnight;
        }

        await till.Db.SaveChangesAsync();

        var handler = new SystemAuditReportQueryHandler(till.Db, till.CurrentUser());
        var pos = await handler.Handle(
            new SystemAuditReportQuery(till.OrganizationId, null, null, null, Today, Today, Channel: SalesChannel.Pos),
            CancellationToken.None);
        Assert.Equal(
            new[] { sale.Id, refund.Id, order }.Order(),
            pos.Items.Select(x => x.DocumentId).Order());

        var erp = await handler.Handle(
            new SystemAuditReportQuery(till.OrganizationId, null, null, null, Today, Today, Channel: SalesChannel.Erp),
            CancellationToken.None);
        Assert.Equal(day.ErpInvoiceId, Assert.Single(erp.Items).DocumentId);

        var yesterday = await handler.Handle(
            new SystemAuditReportQuery(till.OrganizationId, null, null, null, Today.AddDays(-1), Today.AddDays(-1)),
            CancellationToken.None);
        Assert.Empty(yesterday.Items);
    }

    // ---- The restaurant: the Order Report -----------------------------------------------------------------

    [Fact]
    public async Task The_order_report_bills_what_the_day_report_sold_and_the_open_orders_still_owe_their_estimate()
    {
        var restaurant = await PosTestRestaurant.CreateAsync();
        var till = restaurant.Till;
        var session = await till.OpenAsync();

        // Table 1 is billed in two parts by item; table 2 stays open; a take-away is voided whole.
        var split = await restaurant.SeatAsync(restaurant.T1, [restaurant.Momo(2), restaurant.Coke(2)]);
        await restaurant.BillAsync(
            session.Id, split.Id, PosOrderSplit.Items, [till.Cash(316m)],
            [PosTestRestaurant.Line(split, "Chicken Momo", 1m), PosTestRestaurant.Line(split, "Coke 250ml", 1m)]);
        await restaurant.BillAsync(session.Id, split.Id, PosOrderSplit.Whole, [till.Cash(317m)]);
        var open = await restaurant.SeatAsync(restaurant.T2, [restaurant.Momo(1)]);
        var gone = await restaurant.OpenAsync(PosTab.TakeAway, [restaurant.Coke(1)]);
        await restaurant.VoidAsync(gone.Id);

        var report = await new PosOrderReportQueryHandler(till.Db, till.CurrentUser()).Handle(
            new PosOrderReportQuery(till.OrganizationId, Today, Today), CancellationToken.None);

        Assert.Equal((1, 1, 1), (report.OpenCount, report.SettledCount, report.VoidedCount));
        Assert.Equal(3, report.TotalCount);

        // What the orders billed is what the till sold: 316 then 317 (the running total rounded, phase 65),
        // 633 in all, as the day report says.
        var dayReport = await DayReportAsync(till);
        Assert.Equal(633m, report.Billed);
        Assert.Equal(dayReport.Sales.GrandTotal, report.Billed);
        Assert.Equal(1, dayReport.VoidedOrders);

        var settled = report.Items.Single(x => x.Id == split.Id);
        Assert.Equal(2, settled.Invoices.Count);
        Assert.Equal(0m, settled.ToBill);
        Assert.Equal(settled.OrderValue, settled.Billed - settled.BilledRoundOff);

        // The open order owes its estimate, and the dashboard says the same.
        var stillOpen = report.Items.Single(x => x.Id == open.Id);
        Assert.Equal(open.Total, stillOpen.ToBill);
        Assert.Equal(open.Total, report.ToBill);

        var dashboard = await new GetPosDashboardQueryHandler(till.Db).Handle(
            new GetPosDashboardQuery(till.OrganizationId, Today, Today), CancellationToken.None);
        Assert.Equal(1, dashboard.OpenOrders);
        Assert.Equal(report.ToBill, dashboard.OpenOrdersToBill);

        var onlyVoided = await new PosOrderReportQueryHandler(till.Db, till.CurrentUser()).Handle(
            new PosOrderReportQuery(till.OrganizationId, Today, Today, Status: PosOrderStatus.Voided), CancellationToken.None);
        Assert.Equal(gone.Id, Assert.Single(onlyVoided.Items).Id);
        Assert.Equal(0m, onlyVoided.Billed);
    }

    // ---- Validation ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pos_report_refuses_a_backwards_period_and_one_longer_than_a_year()
    {
        var validator = new GetPosDayReportQueryValidator();

        var backwards = await validator.ValidateAsync(new GetPosDayReportQuery(Guid.NewGuid(), Today, Today.AddDays(-1)));
        Assert.Contains(backwards.Errors, x => x.PropertyName == nameof(GetPosDayReportQuery.ToDate));

        var tooLong = await validator.ValidateAsync(
            new GetPosDayReportQuery(Guid.NewGuid(), Today.AddDays(-PosReportPeriod.MaxDays), Today));
        Assert.Contains(tooLong.Errors, x => x.ErrorMessage.Contains("366", StringComparison.Ordinal));

        var aYear = await validator.ValidateAsync(
            new GetPosDayReportQuery(Guid.NewGuid(), Today.AddDays(-(PosReportPeriod.MaxDays - 1)), Today));
        Assert.True(aYear.IsValid);
    }
}
