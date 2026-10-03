using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Sessions;

/// <summary>What the tills took in one payment mode.</summary>
public sealed record PosTenderTotalDto(Guid PaymentModeId, string PaymentModeName, PaymentModeKind Kind, decimal Amount);

/// <summary>
/// The sales figures of a set of till sales -- one session's, one day's, or (phase 66) one period's.
/// </summary>
/// <param name="SubTotal">The lines' amounts after discount, before service charge and VAT.</param>
/// <param name="GrandTotal">What customers owed: sub-total, service charge, VAT and round-off.</param>
/// <param name="Tendered">Everything handed over, in every mode, before change.</param>
/// <param name="Settled">What the tenders settled: tendered less change.</param>
/// <param name="Credit">What was left on customers' accounts: grand total less settled.</param>
/// <param name="CashSales">What the sales put into the drawer: cash tendered less change given.</param>
/// <param name="Refunds">Phase 63 -- the refunds paid out in the same set (session or day).</param>
/// <param name="NetSales">Phase 63 -- what customers owed less what was refunded: the grand total less
/// the refunds' grand total.</param>
/// <param name="Taxable">Phase 66 -- sub-total and service charge on the lines that carry VAT: the Sales
/// Register's taxable bucket, split by the register's own rule (a line with VAT is taxable), so the two
/// agree line for line.</param>
/// <param name="NonTaxable">Phase 66 -- the same on the lines that carry none.</param>
public sealed record PosSalesSummaryDto(
    int SalesCount,
    decimal SubTotal,
    decimal ServiceCharge,
    decimal Vat,
    decimal RoundOff,
    decimal GrandTotal,
    IReadOnlyList<PosTenderTotalDto> Tenders,
    decimal Tendered,
    decimal Change,
    decimal Settled,
    decimal Credit,
    decimal CashSales,
    PosRefundsSummaryDto Refunds,
    decimal NetSales,
    decimal Taxable,
    decimal NonTaxable)
{
    /// <summary>Phase 66 -- the round-off kept, net of the round-off refunds gave back. It is the one
    /// line between a till figure and the Sales Register, which lists supplies and so never carries a
    /// round-off: the register's total for these documents is <see cref="NetSales"/> less this.</summary>
    public decimal NetRoundOff => RoundOff - Refunds.RoundOff;

    /// <summary>Phase 66 -- what the drawers kept in cash: the sales' cash less the refunds' cash.</summary>
    public decimal NetCash => CashSales - Refunds.CashRefunds;

    /// <summary>Phase 66 -- what is still on customers' accounts from these sales: credit left, less what
    /// refunds took back off accounts.</summary>
    public decimal NetCredit => Credit - Refunds.ToAccount;
}

/// <summary>Phase 66 -- one payment mode's takings: what the sales took in it, what refunds paid back in it.</summary>
public sealed record PosPaymentModeLineDto(
    Guid PaymentModeId, string PaymentModeName, PaymentModeKind Kind, decimal Received, decimal PaidBack)
{
    public decimal Net => Received - PaidBack;
}

/// <summary>
/// Phase 66 -- where net sales went, mode by mode. The modes' net, less the change handed back, plus what
/// stayed on customers' accounts, is exactly <see cref="PosSalesSummaryDto.NetSales"/>: the vendor's Day
/// Report listed Cash 349 and Credit 194 (543) under a total of 610.20, and its dashboard Cash 417; here
/// the parts are printed with the line that reconciles them.
/// </summary>
public sealed record PosPaymentBreakdownDto(
    IReadOnlyList<PosPaymentModeLineDto> Modes, decimal Change, decimal Credit)
{
    public decimal Total => Modes.Sum(x => x.Net) - Change + Credit;
}

/// <summary>
/// Phase 63 -- the refunds a till paid out, one session's or one day's.
/// </summary>
/// <param name="GrandTotal">What the refunds gave back in all: sub-total, service charge, VAT and round-off.</param>
/// <param name="Payouts">What was handed back, per mode.</param>
/// <param name="PaidOut">Everything handed back, in every mode.</param>
/// <param name="ToAccount">What came off customers' accounts instead: grand total less paid out.</param>
/// <param name="CashRefunds">What the refunds took out of the drawer.</param>
public sealed record PosRefundsSummaryDto(
    int RefundsCount,
    decimal SubTotal,
    decimal ServiceCharge,
    decimal Vat,
    decimal RoundOff,
    decimal GrandTotal,
    IReadOnlyList<PosTenderTotalDto> Payouts,
    decimal PaidOut,
    decimal ToAccount,
    decimal CashRefunds,
    decimal Taxable,
    decimal NonTaxable)
{
    public static readonly PosRefundsSummaryDto None = new(0, 0m, 0m, 0m, 0m, 0m, [], 0m, 0m, 0m, 0m, 0m);
}

/// <summary>Phase 66 -- how a series of till takings is cut: by the hour of one day, or by day.</summary>
public enum PosSalesBucket
{
    Hour,
    Day,
}

/// <summary>
/// Phase 66 -- one point of a series: what the tills sold, refunded and kept in one hour or one day.
/// <paramref name="Hour"/> is null on a day series.
/// </summary>
public sealed record PosSalesPointDto(DateOnly Date, int? Hour, int SalesCount, decimal Sales, decimal Refunds, decimal Net);

/// <summary>Phase 66 -- a period's figures and its series, read from one fetch.</summary>
public sealed record PosSalesPeriodDto(PosSalesSummaryDto Summary, PosSalesBucket Bucket, IReadOnlyList<PosSalesPointDto> Series);

/// <summary>
/// Phase 61 -- <b>the one place a till's takings are added up</b> (phase 59 Decision H, against the
/// vendor's defect 7). Its Day Report said <i>Total Sales 610.20</i> and its session said <i>Sales
/// Transactions 611.00</i> about the same day, because the two were computed apart: one unrounded
/// and missing a service charge, the other from the invoices. Phase 36's rule is that two figures
/// agree only through one shared reader plus a test that reads both, and this is that reader --
/// <c>ForSessionAsync</c>, <c>ForDayAsync</c> and (phase 66) <c>ForPeriodAsync</c> differ only in which
/// invoices they select and share every line of the arithmetic.
///
/// <para><b>What counts.</b> Approved till sales (<see cref="SalesChannel.Pos"/>). A void takes a sale
/// out: it is as if it never happened, and its reversal takes the cash back out of the drawer's
/// account. That is safe for the session's expected cash only because a void is refused once the
/// session has closed (<c>VoidInvoiceCommandHandler</c>), so a closed session's figures never move.</para>
///
/// <para><b>Shape.</b> Line and tender totals are summed per invoice store-side over the same invoice
/// <i>query</i> (phase 42: join the query, never hand SQL a materialised id list), then added up here.
/// Every money figure is a till figure, so it is already the base currency (Decision E).</para>
///
/// <para><b>Phase 66 -- the series is cut from the same rows as the totals.</b> A chart beside a figure
/// must add up to it (phase 57: a derived series follows the calendar of the figure it agrees with), so a
/// period's points are the very per-document rows its summary was added up from, grouped by the
/// document's own <c>Date</c> -- the till's Nepal business day -- and, for one day, by the Nepal hour it
/// was approved. The hour is only a label: the set is chosen by the date, so the points always sum to the
/// summary.</para>
/// </summary>
internal static class PosSalesReader
{
    public static async Task<PosSalesSummaryDto> ForSessionAsync(
        IAppDbContext db, Guid organizationId, Guid sessionId, CancellationToken cancellationToken) =>
        Summarize(await LoadAsync(
            db,
            TillSales(db, organizationId).Where(x => x.PosSessionId == sessionId),
            TillRefunds(db, organizationId).Where(x => x.PosSessionId == sessionId),
            cancellationToken));

    /// <summary>One business day's till sales, at one location or all of them. The day is the
    /// invoice's own date, which a till stamps with the Nepal date it was rung up on.</summary>
    public static async Task<PosSalesSummaryDto> ForDayAsync(
        IAppDbContext db, Guid organizationId, DateOnly date, Guid? locationId, CancellationToken cancellationToken) =>
        (await ForPeriodAsync(db, organizationId, date, date, locationId, cancellationToken)).Summary;

    /// <summary>
    /// Phase 66 -- a run of business days, with the series that adds up to them: by the hour when the
    /// period is one day, by the day otherwise. Every day in the period has a point, sold or not, so a
    /// chart's axis has no gaps.
    /// </summary>
    public static async Task<PosSalesPeriodDto> ForPeriodAsync(
        IAppDbContext db, Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId,
        CancellationToken cancellationToken)
    {
        var sales = TillSales(db, organizationId).Where(x => x.Date >= fromDate && x.Date <= toDate);
        var refunds = TillRefunds(db, organizationId).Where(x => x.Date >= fromDate && x.Date <= toDate);

        if (locationId is { } onlyLocation)
        {
            sales = sales.Where(x => x.LocationId == onlyLocation);
            refunds = refunds.Where(x => x.LocationId == onlyLocation);
        }

        var facts = await LoadAsync(db, sales, refunds, cancellationToken);
        var bucket = fromDate == toDate ? PosSalesBucket.Hour : PosSalesBucket.Day;

        return new PosSalesPeriodDto(Summarize(facts), bucket, Series(facts, fromDate, toDate, bucket));
    }

    /// <summary>Phase 66 -- <see cref="PosPaymentBreakdownDto"/> from a summary the reader already built.</summary>
    public static PosPaymentBreakdownDto Payments(PosSalesSummaryDto summary)
    {
        var modes = summary.Tenders
            .Select(x => (x.PaymentModeId, x.PaymentModeName, x.Kind))
            .Concat(summary.Refunds.Payouts.Select(x => (x.PaymentModeId, x.PaymentModeName, x.Kind)))
            .Distinct()
            .Select(m => new PosPaymentModeLineDto(
                m.PaymentModeId,
                m.PaymentModeName,
                m.Kind,
                summary.Tenders.Where(x => x.PaymentModeId == m.PaymentModeId).Sum(x => x.Amount),
                summary.Refunds.Payouts.Where(x => x.PaymentModeId == m.PaymentModeId).Sum(x => x.Amount)))
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.PaymentModeName, StringComparer.Ordinal)
            .ToList();

        return new PosPaymentBreakdownDto(modes, summary.Change, summary.NetCredit);
    }

    /// <summary>
    /// What a session's drawer should hold now: the float it opened with, every cash movement, what its
    /// sales put in and what its refunds took out. The one formula both the live session view and the
    /// close use.
    /// </summary>
    public static decimal ExpectedCash(PosSession session, PosSalesSummaryDto sales) =>
        session.OpeningFloat + session.CashMovements.Sum(x => x.SignedAmount) + sales.CashSales
        - sales.Refunds.CashRefunds;

    /// <summary>Approved till sales. Phase 66's reports select from this and nothing else.</summary>
    internal static IQueryable<Invoice> TillSales(IAppDbContext db, Guid organizationId) =>
        db.Invoices.Where(x => x.OrganizationId == organizationId
            && x.Channel == SalesChannel.Pos && x.Status == InvoiceStatus.Approved);

    /// <summary>Phase 63 -- approved till refunds. A voided one is out, as a voided sale is, and for
    /// the same reason it is safe: a refund's void is refused once its session has closed.</summary>
    internal static IQueryable<CreditNote> TillRefunds(IAppDbContext db, Guid organizationId) =>
        db.CreditNotes.Where(x => x.OrganizationId == organizationId
            && x.Channel == SalesChannel.Pos && x.Status == CreditNoteStatus.Approved);

    /// <summary>One document's figures: a sale's, or a refund's.</summary>
    private sealed record DocumentFigures(
        Guid Id,
        DateOnly Date,
        DateTimeOffset At,
        decimal RoundOff,
        decimal Change,
        decimal Amount,
        decimal ServiceCharge,
        decimal Vat,
        decimal Taxable)
    {
        public decimal GrandTotal => Amount + ServiceCharge + Vat + RoundOff;
    }

    private sealed record Facts(
        IReadOnlyList<DocumentFigures> Sales,
        IReadOnlyList<PosTenderTotalDto> Tenders,
        IReadOnlyList<DocumentFigures> Refunds,
        IReadOnlyList<PosTenderTotalDto> Payouts);

    private static async Task<Facts> LoadAsync(
        IAppDbContext db, IQueryable<Invoice> sales, IQueryable<CreditNote> refunds, CancellationToken cancellationToken)
    {
        var saleHeaders = await sales
            .Select(x => new { x.Id, x.Date, x.ApprovedAt, x.CreatedAt, x.RoundOff, x.ChangeAmount })
            .ToListAsync(cancellationToken);

        List<LineTotals> saleLines = saleHeaders.Count == 0
            ? []
            : await (
                    from line in db.InvoiceLines
                    join invoice in sales on line.InvoiceId equals invoice.Id
                    group line by line.InvoiceId into g
                    select new LineTotals(
                        g.Key,
                        g.Sum(x => x.Amount),
                        g.Sum(x => x.ServiceChargeAmount),
                        g.Sum(x => x.VatAmount),
                        g.Sum(x => x.VatAmount != 0m ? x.Amount + x.ServiceChargeAmount : 0m)))
                .ToListAsync(cancellationToken);

        List<ModeTotal> tenderTotals = saleHeaders.Count == 0
            ? []
            : await (
                    from tender in db.InvoiceTenders
                    join invoice in sales on tender.InvoiceId equals invoice.Id
                    group tender by new { tender.PaymentModeId, tender.Kind } into g
                    select new ModeTotal(g.Key.PaymentModeId, g.Key.Kind, g.Sum(x => x.Amount)))
                .ToListAsync(cancellationToken);

        var refundHeaders = await refunds
            .Select(x => new { x.Id, x.Date, x.ApprovedAt, x.CreatedAt, x.RoundOff })
            .ToListAsync(cancellationToken);

        List<LineTotals> refundLines = refundHeaders.Count == 0
            ? []
            : await (
                    from line in db.CreditNoteLines
                    join note in refunds on line.CreditNoteId equals note.Id
                    group line by line.CreditNoteId into g
                    select new LineTotals(
                        g.Key,
                        g.Sum(x => x.Amount),
                        g.Sum(x => x.ServiceChargeAmount),
                        g.Sum(x => x.VatAmount),
                        g.Sum(x => x.VatAmount != 0m ? x.Amount + x.ServiceChargeAmount : 0m)))
                .ToListAsync(cancellationToken);

        List<ModeTotal> payoutTotals = refundHeaders.Count == 0
            ? []
            : await (
                    from payout in db.CreditNotePayouts
                    join note in refunds on payout.CreditNoteId equals note.Id
                    group payout by new { payout.PaymentModeId, payout.Kind } into g
                    select new ModeTotal(g.Key.PaymentModeId, g.Key.Kind, g.Sum(x => x.Amount)))
                .ToListAsync(cancellationToken);

        var modeIds = tenderTotals.Concat(payoutTotals).Select(x => x.PaymentModeId).Distinct().ToList();
        var modeNames = modeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.PaymentModes
                .Where(x => modeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var saleLineById = saleLines.ToDictionary(x => x.DocumentId);
        var refundLineById = refundLines.ToDictionary(x => x.DocumentId);

        return new Facts(
            [.. saleHeaders.Select(h => Figures(h.Id, h.Date, h.ApprovedAt ?? h.CreatedAt, h.RoundOff, h.ChangeAmount, saleLineById))],
            Modes(tenderTotals, modeNames),
            [.. refundHeaders.Select(h => Figures(h.Id, h.Date, h.ApprovedAt ?? h.CreatedAt, h.RoundOff, 0m, refundLineById))],
            Modes(payoutTotals, modeNames));
    }

    private sealed record LineTotals(Guid DocumentId, decimal Amount, decimal ServiceCharge, decimal Vat, decimal Taxable);

    private sealed record ModeTotal(Guid PaymentModeId, PaymentModeKind Kind, decimal Amount);

    private static DocumentFigures Figures(
        Guid id, DateOnly date, DateTimeOffset at, decimal roundOff, decimal change, Dictionary<Guid, LineTotals> lines)
    {
        var totals = lines.GetValueOrDefault(id);
        return new DocumentFigures(
            id, date, at, roundOff, change,
            totals?.Amount ?? 0m, totals?.ServiceCharge ?? 0m, totals?.Vat ?? 0m, totals?.Taxable ?? 0m);
    }

    private static List<PosTenderTotalDto> Modes(List<ModeTotal> totals, Dictionary<Guid, string> modeNames) =>
    [
        .. totals
            .Select(x => new PosTenderTotalDto(x.PaymentModeId, modeNames.GetValueOrDefault(x.PaymentModeId, ""), x.Kind, x.Amount))
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.PaymentModeName, StringComparer.Ordinal),
    ];

    private static PosSalesSummaryDto Summarize(Facts facts)
    {
        var refunds = SummarizeRefunds(facts);
        var sales = facts.Sales;

        var subTotal = sales.Sum(x => x.Amount);
        var serviceCharge = sales.Sum(x => x.ServiceCharge);
        var vat = sales.Sum(x => x.Vat);
        var roundOff = sales.Sum(x => x.RoundOff);
        var taxable = sales.Sum(x => x.Taxable);
        var grandTotal = subTotal + serviceCharge + vat + roundOff;
        var tendered = facts.Tenders.Sum(x => x.Amount);
        var change = sales.Sum(x => x.Change);
        var settled = tendered - change;
        var cashTendered = facts.Tenders.Where(x => x.Kind == PaymentModeKind.Cash).Sum(x => x.Amount);

        return new PosSalesSummaryDto(
            sales.Count,
            subTotal,
            serviceCharge,
            vat,
            roundOff,
            grandTotal,
            facts.Tenders,
            tendered,
            change,
            settled,
            grandTotal - settled,
            cashTendered - change,
            refunds,
            grandTotal - refunds.GrandTotal,
            taxable,
            subTotal + serviceCharge - taxable);
    }

    /// <summary>Phase 63 -- the refunds' half, the same shape as the sales' above.</summary>
    private static PosRefundsSummaryDto SummarizeRefunds(Facts facts)
    {
        var notes = facts.Refunds;
        if (notes.Count == 0)
        {
            return PosRefundsSummaryDto.None;
        }

        var subTotal = notes.Sum(x => x.Amount);
        var serviceCharge = notes.Sum(x => x.ServiceCharge);
        var vat = notes.Sum(x => x.Vat);
        var roundOff = notes.Sum(x => x.RoundOff);
        var taxable = notes.Sum(x => x.Taxable);
        var grandTotal = subTotal + serviceCharge + vat + roundOff;
        var paidOut = facts.Payouts.Sum(x => x.Amount);

        return new PosRefundsSummaryDto(
            notes.Count,
            subTotal,
            serviceCharge,
            vat,
            roundOff,
            grandTotal,
            facts.Payouts,
            paidOut,
            grandTotal - paidOut,
            facts.Payouts.Where(x => x.Kind == PaymentModeKind.Cash).Sum(x => x.Amount),
            taxable,
            subTotal + serviceCharge - taxable);
    }

    /// <summary>
    /// Phase 66 -- the period's points. A document belongs to its own <c>Date</c>; on a one-day series it
    /// is labelled with the Nepal hour it was approved, clamped into that date (a sale stamped 23:59:59.9
    /// and approved a moment after midnight is still the day's last hour, not the next day's first).
    /// </summary>
    private static List<PosSalesPointDto> Series(Facts facts, DateOnly fromDate, DateOnly toDate, PosSalesBucket bucket)
    {
        if (bucket == PosSalesBucket.Hour)
        {
            return
            [
                .. Enumerable.Range(0, 24).Select(hour => Point(
                    fromDate, hour,
                    facts.Sales.Where(x => HourOf(x) == hour).ToList(),
                    facts.Refunds.Where(x => HourOf(x) == hour).ToList())),
            ];
        }

        var points = new List<PosSalesPointDto>();
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var day = date;
            points.Add(Point(
                day, null,
                facts.Sales.Where(x => x.Date == day).ToList(),
                facts.Refunds.Where(x => x.Date == day).ToList()));
        }

        return points;
    }

    private static PosSalesPointDto Point(DateOnly date, int? hour, List<DocumentFigures> sales, List<DocumentFigures> refunds)
    {
        var sold = sales.Sum(x => x.GrandTotal);
        var refunded = refunds.Sum(x => x.GrandTotal);
        return new PosSalesPointDto(date, hour, sales.Count, sold, refunded, sold - refunded);
    }

    private static int HourOf(DocumentFigures document)
    {
        var local = NepalTime.ToLocal(document.At);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        return localDate < document.Date ? 0 : localDate > document.Date ? 23 : local.Hour;
    }
}
